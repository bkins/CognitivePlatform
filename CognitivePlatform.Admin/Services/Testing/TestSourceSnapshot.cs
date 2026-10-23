using System.Diagnostics;

namespace CognitivePlatform.Admin.Services.Testing;

public static class TestSourceSnapshot
{
    public static Dictionary<string, string> Capture(TestOperationsOptions options, TestProfile profile)
    {
        var result = new Dictionary<string, string>();
        // Contract projects can live in another repository while referencing API production code.
        foreach (var relative in profile.Roots.Concat(["CognitivePlatform"]).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var path = Path.Combine(options.RepositoryRoot, relative);
            if (!Directory.Exists(path)) continue;
            var head = Git(path, "rev-parse", "HEAD");
            var status = Git(path, "status", "--porcelain");
            result[relative] = $"commit={head?.Trim() ?? "unavailable"}; dirty={(status is null ? "unknown" : string.IsNullOrWhiteSpace(status) ? "false" : "true")}";
        }
        return result;
    }

    private static string? Git(string directory, params string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory       = directory
          , UseShellExecute        = false
          , CreateNoWindow         = true
          , RedirectStandardOutput = true
          , RedirectStandardError  = true
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = start };
        try
        {
            process.Start();
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(3000)) { process.Kill(true); process.WaitForExit(); return null; }
            Task.WaitAll(output, error);
            return process.ExitCode == 0 ? output.Result : null;
        }
        catch (System.ComponentModel.Win32Exception) { return null; }
    }
}
