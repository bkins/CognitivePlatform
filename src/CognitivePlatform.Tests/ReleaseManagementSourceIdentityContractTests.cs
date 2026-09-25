namespace CognitivePlatform.Tests;

public sealed class ReleaseManagementSourceIdentityContractTests
{
    private static readonly string Page = File.ReadAllText(FindPage());

    [Fact]
    public void ReleaseManagement_UsesConfiguredRepositoriesForReleaseSourceIdentity()
    {
        Assert.Contains("IReleaseSourceIdentityService", Page, StringComparison.Ordinal);
        Assert.Contains("CreateSourceIdentityAsync(componentScope)", Page, StringComparison.Ordinal);
        Assert.Contains("CognitivePlatformCommitHash", Page, StringComparison.Ordinal);
        Assert.Contains("LocalAIAssistantCommitHash", Page, StringComparison.Ordinal);
        Assert.Contains("PayloadManifestSha256", Page, StringComparison.Ordinal);
        Assert.Contains("PayloadFileCount", Page, StringComparison.Ordinal);
        Assert.Contains("TryGetProperty(\"Files\"", Page, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseManagement_DefaultsGitRepositoryFieldToCognitivePlatformRepository()
    {
        Assert.Contains("_gitDirectory = CpRepoRoot", Page, StringComparison.Ordinal);
        Assert.Contains("Release identities are recorded automatically", Page, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseManagement_UsesHeaderEnvironmentAsTheOnlyDeploymentAuthority()
    {
        Assert.Contains("@inject EnvironmentService", Page, StringComparison.Ordinal);
        Assert.Contains("SelectedEnvironment => _operationEnvironment ?? ValidateEnvironment(EnvService.Current)", Page, StringComparison.Ordinal);
        Assert.DoesNotContain("_selectedEnvironment", Page, StringComparison.Ordinal);
        Assert.DoesNotContain("DeployArtifactAsync(capturedArtifact, capturedEnv)", Page, StringComparison.Ordinal);
        Assert.Contains("DeployArtifactAsync(capturedArtifact)", Page, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseManagement_CapturesAndDisplaysTheOperationTarget()
    {
        Assert.Contains("CaptureOperationEnvironment()", Page, StringComparison.Ordinal);
        Assert.Contains("Operation target:", Page, StringComparison.Ordinal);
        Assert.Contains("_operationEnvironment = null", Page, StringComparison.Ordinal);
        Assert.Contains("ValidateEnvironment(EnvService.Current)", Page, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseManagement_RequiresTargetSpecificProductionConfirmation()
    {
        Assert.Contains("ConfirmProductionOperationAsync", Page, StringComparison.Ordinal);
        Assert.Contains("component", Page, StringComparison.Ordinal);
        Assert.Contains("operation", Page, StringComparison.Ordinal);
        Assert.Contains("PROD", Page, StringComparison.Ordinal);
        Assert.Contains("Production target changed before execution", Page, StringComparison.Ordinal);
    }

    private static string FindPage()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "CognitivePlatform.Admin", "Pages", "ReleaseConsole.razor");
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate CognitivePlatform.Admin/Pages/ReleaseConsole.razor from the test output directory.");
    }
}
