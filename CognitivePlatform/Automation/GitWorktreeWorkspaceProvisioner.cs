using System.Diagnostics;
using CP.Shared.Primitives.Avails.Extensions;
using Microsoft.Extensions.Options;

namespace CognitivePlatform.Api.Automation;

public sealed class GitWorktreeWorkspaceProvisioner : IEngineeringWorkspaceProvisioner
{
    private readonly EngineeringPipelineSettings _settings;

    public GitWorktreeWorkspaceProvisioner(IOptions<EngineeringPipelineSettings> settings)
    {
        _settings = settings.Value;
    }

    public async Task<EngineeringWorkspace> ProvisionAsync( string            engineeringRunId
                                                           , string            baseRevision
                                                           , CancellationToken cancellationToken = default )
    {
        if (engineeringRunId.HasNoValue()) throw new EngineeringPipelineException("An engineering run identifier is required.");
        if (baseRevision.HasNoValue() || baseRevision.Trim().StartsWith('-')) throw new EngineeringPipelineException("A valid base revision is required.");
        if (_settings.RepositoryPath.HasNoValue()) throw new EngineeringPipelineException("EngineeringPipeline:RepositoryPath must be configured.");

        var repositoryPath = Path.GetFullPath(_settings.RepositoryPath);
        var workspaceRoot  = Path.GetFullPath(_settings.WorkspaceRoot);
        var workspacePath  = Path.GetFullPath(Path.Combine(workspaceRoot, engineeringRunId));
        EnsureWorkspacePath(workspacePath, workspaceRoot);

        if (Directory.Exists(repositoryPath).Not()) throw new EngineeringPipelineException("The configured engineering repository path does not exist.");
        if (Directory.Exists(workspacePath)) throw new EngineeringPipelineException("The engineering workspace path already exists.");

        Directory.CreateDirectory(workspaceRoot);

        var startInfo = new ProcessStartInfo("git")
                        {
                            WorkingDirectory       = repositoryPath
                          , RedirectStandardOutput = true
                          , RedirectStandardError  = true
                          , UseShellExecute        = false
                          , CreateNoWindow         = true
                        };
        startInfo.ArgumentList.Add("worktree");
        startInfo.ArgumentList.Add("add");
        startInfo.ArgumentList.Add("--detach");
        startInfo.ArgumentList.Add(workspacePath);
        startInfo.ArgumentList.Add(baseRevision.Trim());

        using var process = Process.Start(startInfo)
                            ?? throw new EngineeringPipelineException("Git worktree provisioning could not start.");
        var standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        var standardError = await standardErrorTask.ConfigureAwait(false);
        if (process.ExitCode != 0)
            throw new EngineeringPipelineException($"Git worktree provisioning failed: {standardError.Trim()}");

        return new EngineeringWorkspace(engineeringRunId, workspacePath, baseRevision.Trim());
    }

    private static void EnsureWorkspacePath(string workspacePath, string workspaceRoot)
    {
        var normalizedRoot = workspaceRoot.EndsWith(Path.DirectorySeparatorChar)
            ? workspaceRoot
            : workspaceRoot + Path.DirectorySeparatorChar;
        if (workspacePath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase).Not())
            throw new EngineeringPipelineException("Engineering workspace path must remain under the configured workspace root.");
    }
}
