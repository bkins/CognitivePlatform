using System.Diagnostics;
using System.Security.Cryptography;

namespace CognitivePlatform.Tests;

public sealed class PayloadManifestScriptTests : IDisposable
{
    private readonly string _testRoot = Path.Combine(Path.GetTempPath(), $"cp-payload-manifest-tests-{Guid.NewGuid():N}");

    [Fact]
    public void NewManifest_ChangedApplicationDllWithStableLauncher_ChangesManifestIdentity()
    {
        Directory.CreateDirectory(_testRoot);
        var launcherPath = Path.Combine(_testRoot, "LocalAIAssistant.Ui.Maui.exe");
        var applicationPath = Path.Combine(_testRoot, "LocalAIAssistant.Ui.Maui.dll");
        File.WriteAllText(launcherPath, "stable-launcher");
        File.WriteAllText(applicationPath, "application-v1");
        var launcherHashBefore = ComputeSha256(launcherPath);

        var firstManifestHash = CreateManifest(_testRoot);
        File.WriteAllText(applicationPath, "application-v2");
        var secondManifestHash = CreateManifest(_testRoot);

        Assert.Equal(launcherHashBefore, ComputeSha256(launcherPath));
        Assert.NotEqual(firstManifestHash, secondManifestHash);
    }

    [Fact]
    public void TestManifest_ArtifactAndDeploymentMatchThenChangedPayloadFails()
    {
        var artifactPath = Path.Combine(_testRoot, "artifact");
        var deploymentPath = Path.Combine(_testRoot, "deployment");
        Directory.CreateDirectory(artifactPath);
        File.WriteAllText(Path.Combine(artifactPath, "LocalAIAssistant.Ui.Maui.exe"), "stable-launcher");
        File.WriteAllText(Path.Combine(artifactPath, "LocalAIAssistant.Ui.Maui.dll"), "application-v1");
        var artifactManifestHash = CreateManifest(artifactPath);
        CopyDirectory(artifactPath, deploymentPath);

        var deployedManifestHash = VerifyManifest(deploymentPath);
        File.WriteAllText(Path.Combine(deploymentPath, "LocalAIAssistant.Ui.Maui.dll"), "tampered-application");
        var mismatch = RunPowerShell($". '{EscapePowerShellLiteral(FindManifestTools())}'; Test-PayloadManifest -RootPath '{EscapePowerShellLiteral(deploymentPath)}' | Out-Null");

        Assert.Equal(artifactManifestHash, deployedManifestHash);
        Assert.NotEqual(0, mismatch.ExitCode);
        Assert.Contains("Payload manifest mismatch", mismatch.StandardError + mismatch.StandardOutput, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testRoot)) Directory.Delete(_testRoot, recursive: true);
    }

    private static string CreateManifest(string rootPath)
    {
        var command = $". '{EscapePowerShellLiteral(FindManifestTools())}'; (New-PayloadManifest -RootPath '{EscapePowerShellLiteral(rootPath)}').ManifestSha256";
        var result = RunPowerShell(command);

        Assert.Equal(0, result.ExitCode);
        return result.StandardOutput.Trim();
    }

    private static string VerifyManifest(string rootPath)
    {
        var command = $". '{EscapePowerShellLiteral(FindManifestTools())}'; (Test-PayloadManifest -RootPath '{EscapePowerShellLiteral(rootPath)}').ManifestSha256";
        var result = RunPowerShell(command);

        Assert.Equal(0, result.ExitCode);
        return result.StandardOutput.Trim();
    }

    private static PowerShellResult RunPowerShell(string command)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName               = "pwsh"
          , RedirectStandardOutput = true
          , RedirectStandardError  = true
          , UseShellExecute        = false
          , CreateNoWindow         = true
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(command);
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start pwsh.");
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new PowerShellResult(process.ExitCode, standardOutput, standardError);
    }

    private static string FindManifestTools()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "CognitivePlatform.Admin", "Scripts", "PayloadManifest.ps1");
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate CognitivePlatform.Admin/Scripts/PayloadManifest.ps1 from the test output directory.");
    }

    private static string EscapePowerShellLiteral(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static void CopyDirectory(string sourcePath, string destinationPath)
    {
        Directory.CreateDirectory(destinationPath);
        foreach (var filePath in Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourcePath, filePath);
            var destinationFilePath = Path.Combine(destinationPath, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationFilePath)!);
            File.Copy(filePath, destinationFilePath);
        }
    }

    private sealed record PowerShellResult(int ExitCode, string StandardOutput, string StandardError);
}
