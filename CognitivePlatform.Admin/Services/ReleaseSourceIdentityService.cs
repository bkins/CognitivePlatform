using System.Diagnostics;

namespace CognitivePlatform.Admin.Services;

public sealed class ReleaseSourceIdentityService : IReleaseSourceIdentityService
{
    private readonly string _cognitivePlatformRepo;
    private readonly string _localAiAssistantRepo;

    public ReleaseSourceIdentityService(IConfiguration configuration)
    {
        _cognitivePlatformRepo = configuration["SystemPaths:CognitivePlatformRepo"]
                                  ?? @"C:\Users\benho\source\repos\CognitivePlatform";
        _localAiAssistantRepo = configuration["SystemPaths:LocalAIAssistantRepo"]
                                ?? @"C:\Users\benho\source\repos\LocalAIAssistant";
    }

    public async Task<ReleaseSourceIdentity> CreateSourceIdentityAsync(string componentScope)
    {
        var components = componentScope.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var includesLaa = components.Any(component => component.Equals("Laa", StringComparison.OrdinalIgnoreCase)
                                                   || component.Equals("LaaWindows", StringComparison.OrdinalIgnoreCase));
        var cognitivePlatformCommit = await ReadCommitAsync("CognitivePlatform", _cognitivePlatformRepo);
        var localAiAssistantCommit = includesLaa
            ? await ReadCommitAsync("LocalAIAssistant", _localAiAssistantRepo)
            : null;

        return new ReleaseSourceIdentity(cognitivePlatformCommit, localAiAssistantCommit);
    }

    private static async Task<string> ReadCommitAsync(string repositoryName, string repositoryPath)
    {
        if (!Directory.Exists(repositoryPath))
            throw new InvalidOperationException($"Release source repository not found: {repositoryName} at {repositoryPath}");

        var startInfo = new ProcessStartInfo
        {
            FileName               = "git"
          , Arguments              = "rev-parse HEAD"
          , WorkingDirectory       = repositoryPath
          , RedirectStandardOutput = true
          , RedirectStandardError  = true
          , UseShellExecute        = false
          , CreateNoWindow         = true
        };
        using var process = Process.Start(startInfo)
                            ?? throw new InvalidOperationException($"Could not start git for release source repository {repositoryName}.");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var output = (await outputTask).Trim();
        var error = (await errorTask).Trim();

        if (process.ExitCode != 0 || output.Length != 40 || output.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidOperationException($"Could not read release source identity for {repositoryName}: {error}");

        return output;
    }
}
