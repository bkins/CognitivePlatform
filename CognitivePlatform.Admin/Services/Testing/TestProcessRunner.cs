using System.Diagnostics;

namespace CognitivePlatform.Admin.Services.Testing;

/// <summary>Owns only the child it starts; never discovers or terminates processes by name.</summary>
public sealed class TestProcessRunner : ITestProcessRunner
{
    public async Task<TestProcessResult> RunAsync(ProcessStartInfo  startInfo
                                              , Action<string>    output
                                              , TimeSpan          timeout
                                              , CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        startInfo.UseShellExecute = false;
        startInfo.CreateNoWindow = true;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.StandardOutputEncoding = System.Text.Encoding.UTF8;
        startInfo.StandardErrorEncoding = System.Text.Encoding.UTF8;
        using var process = new Process { StartInfo = startInfo };
        using var timeoutSource = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
        process.Start();
        var stdout = PumpAsync(process.StandardOutput, output);
        var stderr = PumpAsync(process.StandardError, output);
        try
        {
            await process.WaitForExitAsync(linked.Token);
            await Task.WhenAll(stdout, stderr);
            return new TestProcessResult(process.ExitCode);
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { /* Child exited between the check and termination. */ }
            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(stdout, stderr);
            cancellationToken.ThrowIfCancellationRequested();
            return new TestProcessResult(null, TimedOut: true);
        }
    }

    private static async Task PumpAsync(StreamReader reader, Action<string> output)
    {
        while (await reader.ReadLineAsync() is { } line) output(line);
    }
}
