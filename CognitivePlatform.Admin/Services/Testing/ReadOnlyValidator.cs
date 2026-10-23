using System.Diagnostics;
using System.Text.Json;

namespace CognitivePlatform.Admin.Services.Testing;

public sealed class ReadOnlyValidator(TestOperationsOptions options, ITestProcessRunner runner, Func<HttpClient>? clientFactory = null)
{
    public static async Task<ValidationCheck> RuntimeAsync(ValidationTarget target, HttpClient client, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        try
        {
            using var response = await client.GetAsync($"http://localhost:{target.Port}/api/system/environment", HttpCompletionOption.ResponseHeadersRead, token);
            if (!response.IsSuccessStatusCode) return new("Runtime identity", "Unavailable", $"Environment GET returned HTTP {(int)response.StatusCode}.");
            await using var stream = await response.Content.ReadAsStreamAsync(token);
            using var buffer = new MemoryStream();
            var bytes = new byte[4096];
            int count;
            while ((count = await stream.ReadAsync(bytes, token)) > 0)
            {
                if (buffer.Length + count > 65536) return new("Runtime identity", "Failed", "Environment response exceeded limit.");
                buffer.Write(bytes, 0, count);
            }
            using var json = JsonDocument.Parse(buffer.ToArray());
            var data = json.RootElement.GetProperty("data");
            var environment = data.GetProperty("Environment");
            var version = data.GetProperty("Version");
            var expectedEnvironment = target.Environment == "Dev" ? "Development" : "Prod";
            var valid = version.GetProperty("application").GetString() == "CognitivePlatform.Api"
                && version.GetProperty("version").GetString() == target.ApiVersion
                && version.GetProperty("commitSha").GetString() == target.ApiCommit
                && environment.GetProperty("environmentName").GetString() == expectedEnvironment
                && SamePath(environment.GetProperty("databasePath").GetString(), target.Database)
                && SamePath(environment.GetProperty("contentRoot").GetString(), target.ApiDeployment)
                && environment.GetProperty("processId").GetInt32() > 0;
            if (!valid) return new("Runtime identity", "Failed", "Observed application/environment/version/source/database/content-root identity does not match the pinned target.");
            return new("Runtime identity", "Passed", $"{target.Environment}; API {target.ApiVersion}; source {target.ApiCommit}; PID {environment.GetProperty("processId").GetInt32()}; started {environment.GetProperty("startedAtUtc").GetString()}");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (HttpRequestException) { return new("Runtime identity", "Unavailable", "Pinned loopback endpoint is unreachable."); }
        catch (OperationCanceledException) { return new("Runtime identity", "Unavailable", "Environment GET timed out."); }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException)
        { return new("Runtime identity", "Failed", "Environment response is malformed or incomplete."); }
    }

    private static bool SamePath(string? observed, string expected) => observed is not null && string.Equals(Path.GetFullPath(observed).TrimEnd('\\', '/'), Path.GetFullPath(expected).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<ValidationCheck>> ExecuteAsync(ValidationTarget target, bool deployment, Action<string> log, CancellationToken token)
    {
        using var client = clientFactory?.Invoke() ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false }) { Timeout = TimeSpan.FromSeconds(5) };
        var before = await RuntimeAsync(target, client, token);
        var checks = new List<ValidationCheck> { before };
        if (deployment)
        {
            var start = new ProcessStartInfo("pwsh") { WorkingDirectory = options.RepositoryRoot };
            var script = Path.Combine(AppContext.BaseDirectory, "Scripts", "Invoke-ReadOnlyDeploymentValidation.ps1");
            foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-File", script, "-TargetJson", JsonSerializer.Serialize(target) }) start.ArgumentList.Add(argument);
            var output = new List<string>();
            var size = 0;
            var exceeded = false;
            var result = await runner.RunAsync(start, line => { lock (output) { size += line.Length; if (size <= 65536) output.Add(line); else exceeded = true; } }, options.StepTimeout, token);
            if (result.TimedOut) checks.Add(new("Deployment integrity", "TimedOut", "Owned validation worker timed out."));
            else
            {
                try
                {
                    var returned = JsonSerializer.Deserialize<List<ValidationCheck>>(string.Join(Environment.NewLine, output), TestRunStore.JsonOptions);
                    if (exceeded || result.ExitCode != 0 || returned is not { Count: 3 }
                        || returned.Any(check => check.Outcome is not ("Passed" or "Failed" or "Unavailable"))) throw new JsonException();
                    checks.AddRange(returned);
                }
                catch (JsonException) { checks.Add(new("Deployment integrity", "Failed", "Validation worker did not return a valid check report.")); }
            }
        }
        var after = await RuntimeAsync(target, client, token);
        checks.Add(new("Runtime stable during collection", before.Outcome == "Passed" && after == before ? "Passed" : "Failed", before.Outcome == "Passed" && after == before ? "Pinned runtime identity remained unchanged." : "Runtime identity changed or could not be verified at both boundaries."));
        foreach (var check in checks) log($"{check.Name}: {check.Outcome}; {check.Detail}");
        return checks;
    }
}
