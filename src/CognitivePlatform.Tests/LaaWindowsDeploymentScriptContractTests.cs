namespace CognitivePlatform.Tests;

public sealed class LaaWindowsDeploymentScriptContractTests
{
    private static readonly string BuildScript      = File.ReadAllText(FindScript("LAA-Windows-Build.ps1"));
    private static readonly string DeploymentScript = File.ReadAllText(FindScript("LAA-Windows-Deploy.ps1"));
    private static readonly string ManifestTools    = File.ReadAllText(FindScript("PayloadManifest.ps1"));

    [Fact]
    public void Deployment_StagesAndHashesTheArtifactBeforeStoppingTheTargetApp()
    {
        Assert.Contains("$stagingPath", DeploymentScript, StringComparison.Ordinal);
        Assert.Contains("Test-PayloadManifest -RootPath $artifactPathResolved", DeploymentScript, StringComparison.Ordinal);
        Assert.Contains("StartsWith($deployPathWithSeparator", DeploymentScript, StringComparison.Ordinal);
        Assert.Contains("WaitForExit", DeploymentScript, StringComparison.Ordinal);

        var stagingPosition = DeploymentScript.IndexOf("Staging artifact", StringComparison.Ordinal);
        var stopPosition = DeploymentScript.IndexOf("Stopping target LAA process", StringComparison.Ordinal);
        Assert.True(stagingPosition >= 0 && stopPosition > stagingPosition);
    }

    [Fact]
    public void Deployment_UsesAValidatedDirectorySwapAndRestoresThePreviousDeploymentOnFailure()
    {
        Assert.Contains("$allowedDeployPaths", DeploymentScript, StringComparison.Ordinal);
        Assert.Contains("Move-Item -LiteralPath $deployPath -Destination $backupPath", DeploymentScript, StringComparison.Ordinal);
        Assert.Contains("Move-Item -LiteralPath $stagingPath -Destination $deployPath", DeploymentScript, StringComparison.Ordinal);
        Assert.Contains("Restoring previous deployment", DeploymentScript, StringComparison.Ordinal);
        Assert.Contains("Move-Item -LiteralPath $backupPath -Destination $deployPath", DeploymentScript, StringComparison.Ordinal);
        Assert.Contains("deployment.json", DeploymentScript, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_WritesDeterministicPayloadManifestForEveryDeployableFile()
    {
        Assert.Contains("PayloadManifest.ps1", BuildScript, StringComparison.Ordinal);
        Assert.Contains("New-PayloadManifest -RootPath $OutputPath", BuildScript, StringComparison.Ordinal);
        Assert.Contains("payload-manifest.json", ManifestTools, StringComparison.Ordinal);
        Assert.Contains("Get-ChildItem -LiteralPath $resolvedRoot -File -Recurse -Force", ManifestTools, StringComparison.Ordinal);
        Assert.Contains("$relativePaths.Sort([System.StringComparer]::Ordinal)", ManifestTools, StringComparison.Ordinal);
        Assert.Contains("RelativePath", ManifestTools, StringComparison.Ordinal);
        Assert.Contains("Length", ManifestTools, StringComparison.Ordinal);
        Assert.Contains("Sha256", ManifestTools, StringComparison.Ordinal);
    }

    [Fact]
    public void Deployment_VerifiesArtifactStagingAndDeploymentAgainstPayloadManifest()
    {
        Assert.Contains("PayloadManifest.ps1", DeploymentScript, StringComparison.Ordinal);
        Assert.Contains("Test-PayloadManifest -RootPath $artifactPathResolved", DeploymentScript, StringComparison.Ordinal);
        Assert.Contains("Test-PayloadManifest -RootPath $stagingPath", DeploymentScript, StringComparison.Ordinal);
        Assert.Contains("Test-PayloadManifest -RootPath $deployPath", DeploymentScript, StringComparison.Ordinal);
        Assert.Contains("Payload manifest mismatch", DeploymentScript, StringComparison.Ordinal);
        Assert.DoesNotContain("$artifactSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $exeInArtifacts).Hash", DeploymentScript, StringComparison.Ordinal);
    }

    private static string FindScript(string scriptName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "CognitivePlatform.Admin", "Scripts", scriptName);
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate CognitivePlatform.Admin/Scripts/{scriptName} from the test output directory.");
    }
}
