using Moq;
using CognitivePlatform.Api.Domains.DailyRecord;
using CognitivePlatform.Api.Interpreter;
using CognitivePlatform.Api.Models;
using CognitivePlatform.Api.Registry;

namespace CognitivePlatform.Tests;

/// <summary>
/// Tests for FastPathResolver.TryExtractWorkspacePrefix (EPIC-10-D).
/// Verifies that "workspaceName: remainder" is correctly detected as a workspace
/// prefix and that all built-in reserved prefixes are not misidentified.
/// </summary>
public class WorkspacePrefixTests
{
    private readonly Mock<IActionRegistry>           _registryMock    = new();
    private readonly Mock<IDailyRecordCommandParser> _dailyParserMock = new();
    private readonly FastPathResolver                _resolver;

    private static ActionMetadata MakeAction(string name)
        => new() { Name = name, Parameters = new List<ParameterMetadata>() };

    public WorkspacePrefixTests()
    {
        // Register a representative set of action names so the exclusion check works correctly.
        var actions = new List<ActionMetadata>
        {
              MakeAction("AddTask")
            , MakeAction("ListTasks")
            , MakeAction("AddJournalEntry")
            , MakeAction("ListActions")
            , MakeAction("SetModel")
            , MakeAction("SetProvider")
            , MakeAction("UseWorkspace")
            , MakeAction("GetCurrentWorkspace")
            , MakeAction("ListWorkspaces")
        };

        _registryMock.Setup(registry => registry.Actions).Returns(actions);
        _registryMock.Setup(registry => registry.FastPathActions).Returns(new List<ActionMetadata>());

        _resolver = new FastPathResolver(_registryMock.Object, _dailyParserMock.Object);
    }

    // ================================================================
    // Happy path — workspace prefix is extracted correctly
    // ================================================================

    [Fact]
    public void TryExtractWorkspacePrefix_ReturnsTrue_ForSimpleWorkspacePrefix()
    {
        var result = _resolver.TryExtractWorkspacePrefix("work: add task buy milk",
                                                         out var workspaceName,
                                                         out var remainder);

        Assert.True(result);
        Assert.Equal("work", workspaceName);
        Assert.Equal("add task buy milk", remainder);
    }

    [Fact]
    public void TryExtractWorkspacePrefix_ReturnsTrue_ForPersonalPrefix()
    {
        var result = _resolver.TryExtractWorkspacePrefix("personal: list tasks",
                                                         out var workspaceName,
                                                         out var remainder);

        Assert.True(result);
        Assert.Equal("personal", workspaceName);
        Assert.Equal("list tasks", remainder);
    }

    [Fact]
    public void TryExtractWorkspacePrefix_PreservesCase_OfWorkspaceName()
    {
        // Normalisation (lowercase) is WorkspaceContext's responsibility, not the resolver's.
        var result = _resolver.TryExtractWorkspacePrefix("WORK: add task X",
                                                         out var workspaceName,
                                                         out var remainder);

        Assert.True(result);
        Assert.Equal("WORK", workspaceName);
        Assert.Equal("add task X", remainder);
    }

    [Fact]
    public void TryExtractWorkspacePrefix_TrimsRemainder()
    {
        var result = _resolver.TryExtractWorkspacePrefix("work:   add task X",
                                                         out _,
                                                         out var remainder);

        Assert.True(result);
        Assert.Equal("add task X", remainder);
    }

    [Fact]
    public void TryExtractWorkspacePrefix_AcceptsHyphenAndUnderscore_InWorkspaceName()
    {
        var result = _resolver.TryExtractWorkspacePrefix("my-work_space: list tasks",
                                                         out var workspaceName,
                                                         out _);

        Assert.True(result);
        Assert.Equal("my-work_space", workspaceName);
    }

    // ================================================================
    // Daily-record reserved prefixes must not be hijacked
    // ================================================================

    [Theory]
    [InlineData("Plan: get up early today")]
    [InlineData("plan: today I will focus")]
    [InlineData("Check: how are we doing")]
    [InlineData("check: reviewed three items")]
    [InlineData("EOD: wrapping up")]
    [InlineData("eod: done for the day")]
    [InlineData("Done: finished the sprint")]
    [InlineData("done: closed the ticket")]
    [InlineData("Evening: good session")]
    [InlineData("DayDone: all complete")]
    public void TryExtractWorkspacePrefix_ReturnsFalse_ForDailyRecordPrefixes(string input)
    {
        var result = _resolver.TryExtractWorkspacePrefix(input, out _, out _);

        Assert.False(result);
    }

    // ================================================================
    // Registered action names must not be treated as workspace names
    // ================================================================

    [Theory]
    [InlineData("AddTask: buy milk")]
    [InlineData("ListTasks: all")]
    [InlineData("AddJournalEntry: great day")]
    [InlineData("SetModel: gemini")]
    [InlineData("UseWorkspace: work")]
    public void TryExtractWorkspacePrefix_ReturnsFalse_ForRegisteredActionNames(string input)
    {
        var result = _resolver.TryExtractWorkspacePrefix(input, out _, out _);

        Assert.False(result);
    }

    // ================================================================
    // Inputs that should not match the pattern at all
    // ================================================================

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("add task buy milk")]
    [InlineData("work:no-space-after-colon")]
    [InlineData("/work some command")]
    [InlineData("123invalid: prefix starts with digit")]
    public void TryExtractWorkspacePrefix_ReturnsFalse_WhenNoValidPrefixPresent(string input)
    {
        var result = _resolver.TryExtractWorkspacePrefix(input, out _, out _);

        Assert.False(result);
    }

    [Fact]
    public void TryExtractWorkspacePrefix_ReturnsFalse_ForNull()
    {
        var result = _resolver.TryExtractWorkspacePrefix(null!, out _, out _);

        Assert.False(result);
    }
}
