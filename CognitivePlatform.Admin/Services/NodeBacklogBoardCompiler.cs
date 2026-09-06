using System.Diagnostics;

namespace CognitivePlatform.Admin.Services;

public sealed class NodeBacklogBoardCompiler : IBacklogBoardCompiler
{
    private const string CompilerDirectory = @"C:\Users\benho\source\repos\UnifiedBacklogBoard";
    private const string CompilerPath      = @"C:\Users\benho\source\repos\UnifiedBacklogBoard\compile_backlog.js";

    public async Task CompileAsync(CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo("node", $"\"{CompilerPath}\"")
                        {
                            WorkingDirectory       = CompilerDirectory
                          , RedirectStandardError = true
                          , RedirectStandardOutput = true
                          , UseShellExecute        = false
                          , CreateNoWindow         = true
                        };

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardErrorTask  = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);

        var standardOutput = await standardOutputTask;
        var standardError  = await standardErrorTask;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"The backlog compiler failed: {standardError} {standardOutput}".Trim());
        }
    }
}
