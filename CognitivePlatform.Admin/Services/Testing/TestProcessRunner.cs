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
        var stdout = PumpAsync(process.StandardOutput, output, linked.Token);
        var stderr = PumpAsync(process.StandardError, output, linked.Token);
        try
        {
            await process.WaitForExitAsync(linked.Token);
            await Task.WhenAll(stdout, stderr).WaitAsync(linked.Token);
            return new TestProcessResult(process.ExitCode);
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { /* Child exited between the check and termination. */ }
            await process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
            // Descendants may retain inherited pipe handles after the owned parent exits.
            // Cancellation must bound draining as well as waiting for the process itself.
            try { await Task.WhenAll(stdout, stderr); }
            catch (OperationCanceledException) { }
            cancellationToken.ThrowIfCancellationRequested();
            return new TestProcessResult(null, TimedOut: true);
        }
    }

    private static async Task PumpAsync(StreamReader reader, Action<string> output, CancellationToken cancellationToken)
    {
        while (await reader.ReadLineAsync(cancellationToken) is { } line) output(line);
    }
}
