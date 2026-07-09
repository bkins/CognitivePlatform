using CognitivePlatform.Api.Domains.Feedback;
using CognitivePlatform.Api.Interpreter;
using CognitivePlatform.Api.Conversation;
using Microsoft.Extensions.Options;
using Moq;

namespace CognitivePlatform.Tests;

public class FeedbackActionsTests : IDisposable
{
    private readonly string _tempFile;
    private readonly string _tempIdeaFile;
    private readonly Mock<ILlmRouter> _llmRouterMock = new();

    public FeedbackActionsTests()
    {
        _tempFile = Path.Combine(Path.GetTempPath(), $"BugLogTest_{Guid.NewGuid():N}.md");
        _tempIdeaFile = Path.Combine(Path.GetTempPath(), $"IdeaLogTest_{Guid.NewGuid():N}.md");
    }

    public void Dispose()
    {
        if (File.Exists(_tempFile))
            File.Delete(_tempFile);
        if (File.Exists(_tempIdeaFile))
            File.Delete(_tempIdeaFile);
    }

    private FeedbackActions CreateAction(string? filePath = null, string? ideaFilePath = null)
    {
        var settings = new BugReportSettings { FilePath = filePath ?? _tempFile };
        var ideaSettings = new IdeaReportSettings { FilePath = ideaFilePath ?? _tempIdeaFile };
        return new FeedbackActions(Options.Create(settings), Options.Create(ideaSettings), _llmRouterMock.Object);
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
        var action    = CreateAction();
        var beforeUtc = DateTimeOffset.UtcNow;

        action.ReportBug("Timing check.");

        var content = File.ReadAllText(_tempFile);
        Assert.Contains(beforeUtc.ToString("yyyy-MM-dd"), content);
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

    // ================================================================
    // REPORT IDEA
    // ================================================================

    [Fact]
    public void ReportIdea_CreatesFile_WhenFileDoesNotExist()
    {
        var action = CreateAction();

        action.ReportIdea("This is a new feature idea.");

        Assert.True(File.Exists(_tempIdeaFile));
    }

    [Fact]
    public void ReportIdea_WritesDescription_ToFile()
    {
        var action      = CreateAction();
        const string description = "A suggestion to add sorting to the task view.";

        action.ReportIdea(description);

        var content = File.ReadAllText(_tempIdeaFile);
        Assert.Contains(description, content);
    }

    [Fact]
    public void ReportIdea_ReturnsConfirmation_WithFileName()
    {
        var action = CreateAction();

        var result = action.ReportIdea("Confirmation message check.");

        Assert.Contains("Idea logged", result);
        Assert.Contains(Path.GetFileName(_tempIdeaFile), result);
    }

    // ================================================================
    // NEW EXTENDED BUG ACTIONS TESTS
    // ================================================================

    [Fact]
    public void ReportBug_WritesMetadata_ToFile()
    {
        var action = CreateAction();
        action.ReportBug("UI glitch", tags: "UI, visual", severity: "High", context: "Main Screen");

        var content = File.ReadAllText(_tempFile);
        Assert.Contains("- **Status:** Open", content);
        Assert.Contains("- **Severity:** High", content);
        Assert.Contains("- **Tags:** UI, visual", content);
        Assert.Contains("- **Context:** Main Screen", content);
        Assert.Contains("UI glitch", content);
        Assert.Contains("[ID: ", content);
    }

    [Fact]
    public void ListBugs_ReturnsAllBugs_WithFilters()
    {
        var action = CreateAction();
        action.ReportBug("First bug", tags: "UI", severity: "High");
        action.ReportBug("Second bug", tags: "Backend", severity: "Low");

        var allBugs = action.ListBugs();
        Assert.Contains("First bug", allBugs);
        Assert.Contains("Second bug", allBugs);

        var uiBugs = action.ListBugs(tag: "UI");
        Assert.Contains("First bug", uiBugs);
        Assert.DoesNotContain("Second bug", uiBugs);

        var lowBugs = action.ListBugs(severity: "Low");
        Assert.DoesNotContain("First bug", lowBugs);
        Assert.Contains("Second bug", lowBugs);
    }

    [Fact]
    public void TriageBug_UpdatesBugDetails()
    {
        var action = CreateAction();
        action.ReportBug("Triage target", tags: "UI", severity: "Medium");

        // Parse ID from file
        var content = File.ReadAllText(_tempFile);
        var match = System.Text.RegularExpressions.Regex.Match(content, @"\[ID:\s*([A-Z0-9]{4})\]");
        Assert.True(match.Success);
        var id = match.Groups[1].Value;

        var triageResult = action.TriageBug(id, status: "Resolved", notes: "Fixed by refactoring", severity: "Low");
        Assert.Contains("updated successfully", triageResult);

        var updatedContent = File.ReadAllText(_tempFile);
        Assert.Contains("- **Status:** Resolved", updatedContent);
        Assert.Contains("- **Severity:** Low", updatedContent);
        Assert.Contains("- **Triage Notes:** Fixed by refactoring", updatedContent);
    }

    [Fact]
    public void DeleteBug_RemovesBugFromFile()
    {
        var action = CreateAction();
        action.ReportBug("Delete target");

        var content = File.ReadAllText(_tempFile);
        var match = System.Text.RegularExpressions.Regex.Match(content, @"\[ID:\s*([A-Z0-9]{4})\]");
        Assert.True(match.Success);
        var id = match.Groups[1].Value;

        var deleteResult = action.DeleteBug(id);
        Assert.Contains("deleted successfully", deleteResult);

        var updatedContent = File.ReadAllText(_tempFile);
        Assert.DoesNotContain(id, updatedContent);
        Assert.DoesNotContain("Delete target", updatedContent);
    }

    [Fact]
    public async Task SummarizeBugs_CallsLlmRouter()
    {
        var action = CreateAction();
        action.ReportBug("Llm summary target");

        _llmRouterMock.Setup(router => router.SendAsync(It.IsAny<string>(), It.IsAny<ConversationContext>(), It.IsAny<TaskComplexity>(), It.IsAny<CancellationToken>()))
                      .ReturnsAsync(new LlmResponse { Content = "LLM Summary Output" });

        var summary = await action.SummarizeBugs();
        Assert.Equal("LLM Summary Output", summary);

        _llmRouterMock.Verify(router => router.SendAsync(It.Is<string>(p => p.Contains("Llm summary target")), It.IsAny<ConversationContext>(), It.IsAny<TaskComplexity>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SearchBugs_CallsLlmRouter()
    {
        var action = CreateAction();
        action.ReportBug("Llm search target");

        _llmRouterMock.Setup(router => router.SendAsync(It.IsAny<string>(), It.IsAny<ConversationContext>(), It.IsAny<TaskComplexity>(), It.IsAny<CancellationToken>()))
                      .ReturnsAsync(new LlmResponse { Content = "LLM Search Output" });

        var searchResult = await action.SearchBugs("search query");
        Assert.Equal("LLM Search Output", searchResult);

        _llmRouterMock.Verify(router => router.SendAsync(It.Is<string>(p => p.Contains("search query") && p.Contains("Llm search target")), It.IsAny<ConversationContext>(), It.IsAny<TaskComplexity>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}