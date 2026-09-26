namespace CognitivePlatform.Tests;

public sealed class Rel01ArtifactPromotionContractTests
{
    private static readonly string Page = File.ReadAllText(FindPage());

    [Fact]
    public void ReleaseManagement_EnablesDirectoryBackedLaaWindowsArtifacts()
    {
        Assert.Contains("IsLaaWindowsDirectory", Page, StringComparison.Ordinal);
        Assert.Contains("payload-manifest.json", Page, StringComparison.Ordinal);
        Assert.Contains("LocalAIAssistant.Ui.Maui.exe", Page, StringComparison.Ordinal);
        Assert.Contains("ZipPath is not null && File.Exists(ZipPath) || IsLaaWindowsDirectory", Page, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseManagement_PromotesLaaWindowsArtifactWithoutRebuildingOrReserving()
    {
        Assert.Contains("DeployLaaWindowsArtifactAsync", Page, StringComparison.Ordinal);
        Assert.Contains("-ArtifactPath \"\"{artifact.Directory}\"\"", Page, StringComparison.Ordinal);
        Assert.Contains("-Version \"\"{artifact.Version}\"\"", Page, StringComparison.Ordinal);
        Assert.Contains("WriteDeploymentStateAsync(\"LaaWindows\", environment", Page, StringComparison.Ordinal);
        Assert.DoesNotContain("EnsureVersionAsync(\"LaaWindows\")", ExtractMethod("DeployLaaWindowsArtifactAsync"), StringComparison.Ordinal);
        Assert.DoesNotContain("LAA-Windows-Build.ps1", ExtractMethod("DeployLaaWindowsArtifactAsync"), StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseManagement_RecordsPromotedArtifactVersionInDeploymentState()
    {
        Assert.Contains("version: artifact.Version", Page, StringComparison.Ordinal);
        Assert.Contains("Version       = version ?? _version", Page, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseManagement_ReportsPromotedArtifactVersionInPipelineSummary()
    {
        var method = ExtractMethod("DeployLaaWindowsArtifactAsync");

        Assert.Contains("RunCoreAsync(artifact.Directory, artifact.Version", method, StringComparison.Ordinal);
        Assert.Contains("Version:  {summaryVersion ?? _version}", Page, StringComparison.Ordinal);
    }

    private static string ExtractMethod(string methodName)
    {
        var start = Page.IndexOf($"private async Task {methodName}", StringComparison.Ordinal);
        var end   = Page.IndexOf("\n    }", start, StringComparison.Ordinal);
        return Page[start..(end + 6)];
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
