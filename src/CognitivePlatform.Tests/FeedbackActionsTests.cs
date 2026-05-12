using CognitivePlatform.Api.Domains.Feedback;
using Microsoft.Extensions.Options;

namespace CognitivePlatform.Tests;

public class FeedbackActionsTests : IDisposable
{
    private readonly string _tempFile;

    public FeedbackActionsTests()
    {
        _tempFile = Path.Combine(Path.GetTempPath(), $"BugLogTest_{Guid.NewGuid():N}.md");
    }

    public void Dispose()
    {
        if (File.Exists(_tempFile))
            File.Delete(_tempFile);
    }

    private FeedbackActions CreateAction(string? filePath = null)
    {
        var settings = new BugReportSettings { FilePath = filePath ?? _tempFile };
        return new FeedbackActions(Options.Create(settings));
    }

    // ================================================================
    // REPORT BUG — happy path
    // ================================================================

    [Fact]
    public void ReportBug_CreatesFile_WhenFileDoesNotExist()
    {
        var action = CreateAction();

        action.ReportBug("The submit button crashes the app.");

        Assert.True(File.Exists(_tempFile));
    }

    [Fact]
    public void ReportBug_WritesDescription_ToFile()
    {
        var action      = CreateAction();
        const string description = "Search returns empty results even when items exist.";

        action.ReportBug(description);

        var content = File.ReadAllText(_tempFile);
        Assert.Contains(description, content);
    }

    [Fact]
    public void ReportBug_AppendsMultipleEntries_WhenCalledTwice()
    {
        var action = CreateAction();

        action.ReportBug("First bug description.");
        action.ReportBug("Second bug description.");

        var content = File.ReadAllText(_tempFile);
        Assert.Contains("First bug description.", content);
        Assert.Contains("Second bug description.", content);
    }

    [Fact]
    public void ReportBug_WritesHeader_OnFirstUse()
    {
        var action = CreateAction();

        action.ReportBug("Any bug.");

        var content = File.ReadAllText(_tempFile);
        Assert.Contains("# Bug Log", content);
    }

    [Fact]
    public void ReportBug_WritesTimestamp_InEntry()
    {
        var action      = CreateAction();
        var beforeLocal = DateTimeOffset.Now;

        action.ReportBug("Timing check.");

        var content = File.ReadAllText(_tempFile);
        Assert.Contains(beforeLocal.ToString("yyyy-MM-dd"), content);
    }

    [Fact]
    public void ReportBug_ReturnsConfirmation_WithFileName()
    {
        var action = CreateAction();

        var result = action.ReportBug("Confirmation message check.");

        Assert.Contains("Bug logged", result);
        Assert.Contains(Path.GetFileName(_tempFile), result);
    }

    // ================================================================
    // REPORT BUG — guard clauses
    // ================================================================

    [Fact]
    public void ReportBug_ReturnsError_WhenDescriptionIsEmpty()
    {
        var action = CreateAction();

        var result = action.ReportBug(string.Empty);

        Assert.Contains("empty", result, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(_tempFile));
    }

    [Fact]
    public void ReportBug_ReturnsError_WhenFilePathNotConfigured()
    {
        var action = CreateAction(filePath: string.Empty);

        var result = action.ReportBug("This should not be written.");

        Assert.Contains("not configured", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReportBug_TrimsDescription_BeforePersisting()
    {
        var action = CreateAction();

        action.ReportBug("   padded description   ");

        var content = File.ReadAllText(_tempFile);
        Assert.Contains("padded description", content);
    }
}