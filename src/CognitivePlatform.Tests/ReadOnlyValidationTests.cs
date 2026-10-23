using System.Text.Json;
using System.Net;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using CognitivePlatform.Admin.Services;
using Moq;
using CognitivePlatform.Admin.Services.Testing;

namespace CognitivePlatform.Tests;

public sealed class ReadOnlyValidationTests
{
    [Fact]
    public void Contract_Evidence_Also_Captures_Referenced_Admin_And_Api_Source_Repository()
    {
        var root = Path.Combine(Path.GetTempPath(), $"cp-source-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "CognitivePlatform"));
        Directory.CreateDirectory(Path.Combine(root, "contracts"));
        try
        {
            var sources = TestSourceSnapshot.Capture(new() { RepositoryRoot = root }, new("routing-contracts", "isolated", ["contracts"]));
            Assert.Contains("CognitivePlatform", sources.Keys);
            Assert.Contains("contracts", sources.Keys);
        }
        finally { Directory.Delete(root, true); }
    }

    private static readonly ValidationTarget Target = ValidationTarget.ForProfile("runtime-dev");

    [Theory]
    [InlineData("valid", "Passed")]
    [InlineData("api-tamper", "Failed")]
    [InlineData("api-extra", "Failed")]
    [InlineData("windows-tamper", "Failed")]
    [InlineData("windows-extra", "Failed")]
    [InlineData("unsafe-manifest", "Failed")]
    [InlineData("missing", "Unavailable")]
    [InlineData("configuration", "Failed")]
    [InlineData("launcher-tamper", "Failed")]
    [InlineData("linked-payload", "Failed")]
    public async Task Canonical_Deployment_Inspection_Is_ReadOnly_And_Rejects_Corrupt_Or_Missing_Evidence(string scenario, string expected)
    {
        var root = Path.Combine(Path.GetTempPath(), $"cp-integrity-{Guid.NewGuid():N}");
        var apiArtifact = Path.Combine(root, "api-artifact");
        var apiDeployment = Path.Combine(root, "api-deployment");
        var windowsArtifact = Path.Combine(root, "windows-artifact");
        var windowsDeployment = Path.Combine(root, "windows-deployment");
        foreach (var folder in new[] { apiArtifact, apiDeployment, windowsArtifact, windowsDeployment }) Directory.CreateDirectory(folder);
        try
        {
            foreach (var folder in new[] { apiArtifact, apiDeployment }) File.WriteAllText(Path.Combine(folder, "payload.bin"), "api");
            var database = Path.Combine(root, "data", "platform.db");
            File.WriteAllText(Path.Combine(apiDeployment, "appsettings.json"), JsonSerializer.Serialize(new { SystemPaths = new { DataRoot = Path.Combine(root, "data") }, credential = "private-fixture-value" }));
            File.WriteAllText(Path.Combine(apiDeployment, "appsettings.Development.json"), JsonSerializer.Serialize(new { SystemPaths = new { DatabasePath = scenario == "configuration" ? Path.Combine(root, "wrong.db") : database } }));
            File.WriteAllText(Path.Combine(apiDeployment, "environment.json"), "fixture-environment");
            File.WriteAllText(Path.Combine(apiDeployment, "start-api.ps1"), "fixture-launcher");
            foreach (var folder in new[] { windowsArtifact, windowsDeployment })
            {
                File.WriteAllText(Path.Combine(folder, "payload.bin"), "windows");
                File.WriteAllText(Path.Combine(folder, "build-environment.json"), JsonSerializer.Serialize(new { SchemaVersion = 1, BuiltFor = "Dev", Version = Target.WindowsVersion, ApiBaseUrl = "http://localhost:5273" }));
            }
            string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
            var manifest = JsonSerializer.Serialize(new { SchemaVersion = 1, Algorithm = "SHA256", Files = new[] { "payload.bin", "build-environment.json" }.Select(name => new { RelativePath = scenario == "unsafe-manifest" && name == "payload.bin" ? "../outside.bin" : name, Length = new FileInfo(Path.Combine(windowsArtifact, name)).Length, Sha256 = Hash(Path.Combine(windowsArtifact, name)) }) });
            foreach (var folder in new[] { windowsArtifact, windowsDeployment }) File.WriteAllText(Path.Combine(folder, "payload-manifest.json"), manifest);
            var apiDigest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("payload.bin\t" + Hash(Path.Combine(apiArtifact, "payload.bin")) + "\n")));
            var target = Target with { ApiArtifact = apiArtifact, ApiDeployment = apiDeployment, ApiInventorySha256 = apiDigest, Database = database, WindowsArtifact = windowsArtifact, WindowsDeployment = windowsDeployment, WindowsManifestSha256 = Hash(Path.Combine(windowsArtifact, "payload-manifest.json")), ApiEnvironmentSha256 = Hash(Path.Combine(apiDeployment, "environment.json")), ApiLauncherSha256 = Hash(Path.Combine(apiDeployment, "start-api.ps1")) };
            if (scenario == "api-tamper") File.WriteAllText(Path.Combine(apiDeployment, "payload.bin"), "changed");
            if (scenario == "api-extra") File.WriteAllText(Path.Combine(apiDeployment, "extra.bin"), "unexpected");
            if (scenario == "windows-tamper") File.WriteAllText(Path.Combine(windowsDeployment, "payload.bin"), "changed");
            if (scenario == "windows-extra") File.WriteAllText(Path.Combine(windowsDeployment, "extra.bin"), "unexpected");
            if (scenario == "missing") File.Delete(Path.Combine(windowsDeployment, "payload-manifest.json"));
            if (scenario == "launcher-tamper") File.WriteAllText(Path.Combine(apiDeployment, "start-api.ps1"), "changed-launcher");
            if (scenario == "linked-payload")
            {
                var junction = new ProcessStartInfo("pwsh");
                var command = $"New-Item -ItemType Junction -Path '{Path.Combine(apiDeployment, "payload-link").Replace("'", "''")}' -Target '{windowsArtifact.Replace("'", "''")}' | Out-Null";
                foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(command)) }) junction.ArgumentList.Add(argument);
                var linked = await new TestProcessRunner().RunAsync(junction, _ => { }, TimeSpan.FromSeconds(10), CancellationToken.None);
                Assert.Equal(0, linked.ExitCode);
            }
            var before = Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToDictionary(path => path, Hash);
            var script = Path.Combine(AdminLifecycleSafetyTests.RepositoryRoot, "CognitivePlatform", "CognitivePlatform.Admin", "Scripts", "Invoke-ReadOnlyDeploymentValidation.ps1");
            var start = new ProcessStartInfo("pwsh");
            foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-File", script, "-TargetJson", JsonSerializer.Serialize(target) }) start.ArgumentList.Add(argument);
            var lines = new System.Collections.Concurrent.ConcurrentQueue<string>();
            var result = await new TestProcessRunner().RunAsync(start, lines.Enqueue, TimeSpan.FromSeconds(30), CancellationToken.None);
            var output = string.Join(Environment.NewLine, lines);
            Assert.True(result.ExitCode == 0, output);
            Assert.DoesNotContain("private-fixture-value", output);
            var checks = JsonSerializer.Deserialize<List<ValidationCheck>>(output, TestRunStore.JsonOptions)!;
            Assert.Equal(3, checks.Count);
            if (expected == "Passed") Assert.All(checks, check => Assert.Equal("Passed", check.Outcome));
            else Assert.Contains(checks, check => check.Outcome == expected);
            Assert.Equal(before.Count, Directory.GetFiles(root, "*", SearchOption.AllDirectories).Length);
            Assert.All(before, pair => Assert.Equal(pair.Value, Hash(pair.Key)));
        }
        finally
        {
            var link = Path.Combine(apiDeployment, "payload-link");
            if (Directory.Exists(link)) Directory.Delete(link);
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static string RuntimeJson(string field = "valid", int pid = 123) => JsonSerializer.Serialize(new
    {
        data = new
        {
            Environment = new { environmentName = field == "environment" ? "Prod" : "Development", databasePath = field == "database" ? @"C:\CP\Data\Prod\platform.db" : Target.Database,
                contentRoot = field == "root" ? @"C:\Unexpected" : Target.ApiDeployment, processId = pid, startedAtUtc = "2026-10-22T00:00:00Z" },
            Version = new { application = field == "application" ? "Other" : "CognitivePlatform.Api", version = field == "version" ? "0.0.0" : Target.ApiVersion, commitSha = field == "source" ? "different" : Target.ApiCommit }
        }
    });

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }

    [Theory]
    [InlineData("valid", "Passed")]
    [InlineData("environment", "Failed")]
    [InlineData("database", "Failed")]
    [InlineData("root", "Failed")]
    [InlineData("application", "Failed")]
    [InlineData("version", "Failed")]
    [InlineData("source", "Failed")]
    [InlineData("malformed", "Failed")]
    public async Task Runtime_Validates_Independent_Pinned_Identity(string field, string expected)
    {
        using var client = new HttpClient(new Handler((request, token) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("http://localhost:5273/api/system/environment", request.RequestUri!.AbsoluteUri);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(field == "malformed" ? "{}" : RuntimeJson(field)) });
        }));
        var check = await ReadOnlyValidator.RuntimeAsync(Target, client, CancellationToken.None);
        Assert.Equal(expected, check.Outcome);
    }

    [Fact]
    public async Task Runtime_Unavailable_And_Cancellation_Are_Honest()
    {
        using var client = new HttpClient(new Handler((_, token) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))));
        Assert.Equal("Unavailable", (await ReadOnlyValidator.RuntimeAsync(Target, client, CancellationToken.None)).Outcome);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ReadOnlyValidator.RuntimeAsync(Target, client, cancellation.Token));
    }

    [Fact]
    public async Task Runtime_Process_Change_During_Collection_Fails_Stability_Check()
    {
        var calls = 0;
        var validator = new ReadOnlyValidator(new(), Mock.Of<ITestProcessRunner>(), () => new HttpClient(new Handler((_, token) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(RuntimeJson(pid: ++calls)) }))));
        var checks = await validator.ExecuteAsync(Target, false, _ => { }, CancellationToken.None);
        Assert.Equal("Passed", checks[0].Outcome);
        Assert.Equal("Failed", checks[1].Outcome);
    }

    [Theory]
    [InlineData("Passed", TestRunStatus.Passed)]
    [InlineData("Failed", TestRunStatus.Failed)]
    [InlineData("Unavailable", TestRunStatus.Failed)]
    [InlineData("TimedOut", TestRunStatus.TimedOut)]
    public async Task Shared_Coordinator_Records_Checks_Without_Fabricated_Tests_And_Exports_Durable_Evidence(string outcome, TestRunStatus status)
    {
        var root = Path.Combine(Path.GetTempPath(), $"cp-validation-{Guid.NewGuid():N}");
        var options = new TestOperationsOptions { RootPath = Path.Combine(root, "runs"), RepositoryRoot = Path.Combine(root, "repos"), Secret = "test-secret-value" };
        try
        {
            using var coordinator = new TestRunCoordinator(options, new(options), new(options), Mock.Of<ITestProcessRunner>(), new TerminalStateService(),
                (_, _, _, _) => Task.FromResult<IReadOnlyList<ValidationCheck>>([new("fixture", outcome, "test-secret-value")]));
            var run = coordinator.Start(new("runtime-dev", Guid.NewGuid()));
            for (var attempt = 0; attempt < 200 && coordinator.Get(run.Id)!.FinishedAt is null; attempt++) await Task.Delay(10);
            var finished = coordinator.Get(run.Id)!;
            Assert.Equal(status, finished.Status);
            Assert.NotNull(finished.Target);
            Assert.Single(finished.Checks);
            Assert.DoesNotContain("test-secret-value", finished.Checks[0].Detail);
            Assert.Equal(0, finished.Passed + finished.Failed);
            Assert.Empty(finished.Projects);
            var evidenceText = coordinator.Evidence(run.Id);
            Assert.DoesNotContain("test-secret-value", evidenceText);
            using var evidence = JsonDocument.Parse(evidenceText);
            Assert.Equal(run.Id, evidence.RootElement.GetProperty("runId").GetGuid());
            var markdown = evidence.RootElement.GetProperty("markdown").GetString()!;
            Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(markdown))), evidence.RootElement.GetProperty("reportSha256").GetString());
            using var reloaded = new TestRunCoordinator(options, new(options), new(options), Mock.Of<ITestProcessRunner>(), new TerminalStateService());
            Assert.Equal(evidenceText, reloaded.Evidence(run.Id));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Validation_Cancel_Stops_Accepted_Run_And_Exports_Cancelled_State()
    {
        var root = Path.Combine(Path.GetTempPath(), $"cp-check-cancel-{Guid.NewGuid():N}");
        var options = new TestOperationsOptions { RootPath = Path.Combine(root, "runs"), RepositoryRoot = Path.Combine(root, "repos") };
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            using var coordinator = new TestRunCoordinator(options, new(options), new(options), Mock.Of<ITestProcessRunner>(), new TerminalStateService(), async (_, _, _, token) =>
            {
                entered.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return [];
            });
            var run = coordinator.Start(new("deployment-prod", Guid.NewGuid()));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Throws<InvalidOperationException>(() => coordinator.Evidence(run.Id));
            Assert.Throws<InvalidOperationException>(() => coordinator.Start(new("unit", Guid.NewGuid())));
            Assert.True(coordinator.Cancel(run.Id));
            for (var attempt = 0; attempt < 200 && coordinator.Get(run.Id)!.FinishedAt is null; attempt++) await Task.Delay(10);
            Assert.Equal(TestRunStatus.Cancelled, coordinator.Get(run.Id)!.Status);
            Assert.Contains("Cancelled", coordinator.Evidence(run.Id));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("runtime-dev")]
    [InlineData("runtime-prod")]
    [InlineData("deployment-dev")]
    [InlineData("deployment-prod")]
    [InlineData("routing-contracts")]
    public void Named_Profile_Is_Explicitly_Registered(string profile)
    {
        var catalog = new TestProjectCatalog(new TestOperationsOptions());
        Assert.Contains(catalog.Profiles, item => item.Id == profile);
    }

    [Fact]
    public void Evidence_Distinguishes_Validation_Checks_From_Unit_Test_Counts()
    {
        var json = JsonSerializer.SerializeToElement(new TestRun(), TestRunStore.JsonOptions);
        Assert.True(json.TryGetProperty("checks", out _));
        Assert.True(json.TryGetProperty("target", out _));
    }
}
