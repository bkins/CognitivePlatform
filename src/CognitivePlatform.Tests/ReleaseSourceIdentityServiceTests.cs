using System.Diagnostics;
using CognitivePlatform.Admin.Services;
using Microsoft.Extensions.Configuration;

namespace CognitivePlatform.Tests;

public sealed class ReleaseSourceIdentityServiceTests : IDisposable
{
    private readonly string _testRoot = Path.Combine(Path.GetTempPath(), $"cp-release-source-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task CreateSourceIdentityAsync_LaaWindows_RecordsCognitivePlatformAndLaaCommits()
    {
        var cognitivePlatformRepo = CreateRepository("CognitivePlatform", "cp-source");
        var localAiAssistantRepo = CreateRepository("LocalAIAssistant", "laa-source");
        var service = CreateService(cognitivePlatformRepo, localAiAssistantRepo);

        var result = await service.CreateSourceIdentityAsync("LaaWindows");

        Assert.Equal(ReadCommit(cognitivePlatformRepo), result.CognitivePlatformCommitHash);
        Assert.Equal(ReadCommit(localAiAssistantRepo), result.LocalAIAssistantCommitHash);
        Assert.Equal($"CognitivePlatform={result.CognitivePlatformCommitHash}|LocalAIAssistant={result.LocalAIAssistantCommitHash}", result.Value);
    }

    [Fact]
    public async Task CreateSourceIdentityAsync_Api_RecordsOnlyCognitivePlatformCommit()
    {
        var cognitivePlatformRepo = CreateRepository("CognitivePlatform", "cp-source");
        var localAiAssistantRepo = CreateRepository("LocalAIAssistant", "laa-source");
        var service = CreateService(cognitivePlatformRepo, localAiAssistantRepo);

        var result = await service.CreateSourceIdentityAsync("API");

        Assert.Equal(ReadCommit(cognitivePlatformRepo), result.CognitivePlatformCommitHash);
        Assert.Null(result.LocalAIAssistantCommitHash);
        Assert.Equal($"CognitivePlatform={result.CognitivePlatformCommitHash}", result.Value);
    }

    public void Dispose()
    {
        if (!Directory.Exists(_testRoot)) return;

        foreach (var filePath in Directory.GetFiles(_testRoot, "*", SearchOption.AllDirectories))
            File.SetAttributes(filePath, FileAttributes.Normal);
        Directory.Delete(_testRoot, recursive: true);
    }

    private ReleaseSourceIdentityService CreateService(string cognitivePlatformRepo, string localAiAssistantRepo)
    {
        var configuration = new ConfigurationBuilder()
                           .AddInMemoryCollection(new Dictionary<string, string?>
                           {
                               ["SystemPaths:CognitivePlatformRepo"] = cognitivePlatformRepo
                             , ["SystemPaths:LocalAIAssistantRepo"]  = localAiAssistantRepo
                           })
                           .Build();
        return new ReleaseSourceIdentityService(configuration);
    }

    private string CreateRepository(string name, string content)
    {
        var repositoryPath = Path.Combine(_testRoot, name);
        Directory.CreateDirectory(repositoryPath);
        RunGit(repositoryPath, "init");
        RunGit(repositoryPath, "config", "user.email", "tests@example.invalid");
        RunGit(repositoryPath, "config", "user.name", "CP Tests");
        File.WriteAllText(Path.Combine(repositoryPath, "source.txt"), content);
        RunGit(repositoryPath, "add", "source.txt");
        RunGit(repositoryPath, "commit", "-m", "Test source");
        return repositoryPath;
    }

    private static string ReadCommit(string repositoryPath)
    {
        return RunGit(repositoryPath, "rev-parse", "HEAD").Trim();
    }

    private static string RunGit(string repositoryPath, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName               = "git"
          , WorkingDirectory       = repositoryPath
          , RedirectStandardOutput = true
          , RedirectStandardError  = true
          , UseShellExecute        = false
          , CreateNoWindow         = true
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start git.");
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException(standardError);
        return standardOutput;
    }
}
