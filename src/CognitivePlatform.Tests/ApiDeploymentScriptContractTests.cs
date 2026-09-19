namespace CognitivePlatform.Tests;

public sealed class ApiDeploymentScriptContractTests
{
    private static readonly string Script = File.ReadAllText(FindScript());

    [Fact]
    public void Deployment_StopsOnlyTheApiRunningFromTheTargetDirectory()
    {
        Assert.Contains("Get-Process -Name $processName", Script, StringComparison.Ordinal);
        Assert.Contains("StartsWith($deployPathWithSeparator", Script, StringComparison.Ordinal);
        Assert.Contains("WaitForExit", Script, StringComparison.Ordinal);
    }

    [Fact]
    public void Deployment_StagesAndValidatesTheArtifactBeforeReplacingTheLiveDirectory()
    {
        Assert.Contains("$stagingPath", Script, StringComparison.Ordinal);
        Assert.Contains("Get-FileHash -Algorithm SHA256", Script, StringComparison.Ordinal);
        Assert.Contains("Move-Item -LiteralPath $deployPath -Destination $backupPath", Script, StringComparison.Ordinal);
        Assert.Contains("Move-Item -LiteralPath $stagingPath -Destination $deployPath", Script, StringComparison.Ordinal);
    }

    [Fact]
    public void Deployment_RestoresThePreviousDirectoryWhenTheSwapFails()
    {
        Assert.Contains("Restoring previous deployment", Script, StringComparison.Ordinal);
        Assert.Contains("Move-Item -LiteralPath $backupPath -Destination $deployPath", Script, StringComparison.Ordinal);
        Assert.Contains("deployment.json", Script, StringComparison.Ordinal);
    }

    private static string FindScript()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "CognitivePlatform.Admin", "Scripts", "API-Deploy.ps1");
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate CognitivePlatform.Admin/Scripts/API-Deploy.ps1 from the test output directory.");
    }
}
