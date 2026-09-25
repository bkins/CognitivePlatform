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
