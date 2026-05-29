using Moq;
using CognitivePlatform.Api.Domains.DailyRecord;
using CognitivePlatform.Api.Interpreter;
using CognitivePlatform.Api.Models;
using CognitivePlatform.Api.Registry;

namespace CognitivePlatform.Tests;

public class GuidanceFastPathTests
{
    private readonly Mock<IActionRegistry>           _registryMock    = new();
    private readonly Mock<IDailyRecordCommandParser> _dailyParserMock = new();
    private readonly FastPathResolver                _resolver;

    private static ActionMetadata MakeAction(string name)
        => new() { Name = name, Parameters = new List<ParameterMetadata>() };

    public GuidanceFastPathTests()
    {
        var actions = new List<ActionMetadata>
        {
                MakeAction("ListActions")
              , MakeAction("GetDomainGuidance")
              , MakeAction("AddJournalEntry")
              , MakeAction("ListJournalEntries")
              , MakeAction("AddTask")
              , MakeAction("ListTasks")
              , MakeAction("OpenDay")
              , MakeAction("AddCheckpoint")
              , MakeAction("CloseDay")
              , MakeAction("ClaimRolledOverTasks")
        };

        _registryMock.Setup(registry => registry.Actions).Returns(actions);
        _registryMock.Setup(registry => registry.FastPathActions).Returns(new List<ActionMetadata>());

        _resolver = new FastPathResolver(_registryMock.Object, _dailyParserMock.Object);
    }

    // ================================================================
    // "Tell me about X" phrases
    // ================================================================

    [Fact]
    public void TryResolve_ResolvesToGetDomainGuidance_ForTellMeAboutTasks()
    {
        var resolved = _resolver.TryResolve("Tell me about Tasks", out var action, out var parameters);

        Assert.True(resolved);
        Assert.Equal("GetDomainGuidance", action!.Name);
        Assert.Equal("Tasks",             parameters!["domainName"]);
    }

    [Fact]
    public void TryResolve_ResolvesToGetDomainGuidance_ForTellMeAboutJournal()
    {
        var resolved = _resolver.TryResolve("Tell me about Journal", out var action, out var parameters);

        Assert.True(resolved);
        Assert.Equal("GetDomainGuidance", action!.Name);
        Assert.Equal("Journal",           parameters!["domainName"]);
    }

    [Fact]
    public void TryResolve_ResolvesToGetDomainGuidance_ForTellMeAboutTheCalendar()
    {
        var resolved = _resolver.TryResolve("Tell me about the calendar", out var action, out var parameters);

        Assert.True(resolved);
        Assert.Equal("GetDomainGuidance", action!.Name);
        Assert.Equal("Calendar",          parameters!["domainName"]);
    }

    // ================================================================
    // "How does X work?" phrases
    // ================================================================

    [Fact]
    public void TryResolve_ResolvesToGetDomainGuidance_ForHowDoesTasksWork()
    {
        var resolved = _resolver.TryResolve("How does tasks work?", out var action, out var parameters);

        Assert.True(resolved);
        Assert.Equal("GetDomainGuidance", action!.Name);
        Assert.Equal("Tasks",             parameters!["domainName"]);
    }

    [Fact]
    public void TryResolve_ResolvesToGetDomainGuidance_ForHowDoesJournalWork()
    {
        var resolved = _resolver.TryResolve("How does journal work?", out var action, out var parameters);

        Assert.True(resolved);
        Assert.Equal("GetDomainGuidance", action!.Name);
        Assert.Equal("Journal",           parameters!["domainName"]);
    }

    // ================================================================
    // "What can I do with X?" phrases
    // ================================================================

    [Fact]
    public void TryResolve_ResolvesToGetDomainGuidance_ForWhatCanIDoWithMyCalendar()
    {
        var resolved = _resolver.TryResolve("What can I do with my calendar?", out var action, out var parameters);

        Assert.True(resolved);
        Assert.Equal("GetDomainGuidance", action!.Name);
        Assert.Equal("Calendar",          parameters!["domainName"]);
    }

    [Fact]
    public void TryResolve_ResolvesToGetDomainGuidance_ForWhatCanIDoWithTasks()
    {
        var resolved = _resolver.TryResolve("What can I do with tasks?", out var action, out var parameters);

        Assert.True(resolved);
        Assert.Equal("GetDomainGuidance", action!.Name);
        Assert.Equal("Tasks",             parameters!["domainName"]);
    }

    // ================================================================
    // "Explain X" phrases
    // ================================================================

    [Fact]
    public void TryResolve_ResolvesToGetDomainGuidance_ForExplainTasks()
    {
        var resolved = _resolver.TryResolve("Explain tasks", out var action, out var parameters);

        Assert.True(resolved);
        Assert.Equal("GetDomainGuidance", action!.Name);
        Assert.Equal("Tasks",             parameters!["domainName"]);
    }

    [Fact]
    public void TryResolve_ResolvesToGetDomainGuidance_ForExplainHowJournalWorks()
    {
        var resolved = _resolver.TryResolve("Explain how journal works", out var action, out var parameters);

        Assert.True(resolved);
        Assert.Equal("GetDomainGuidance", action!.Name);
        Assert.Equal("Journal",           parameters!["domainName"]);
    }

    // ================================================================
    // "How do I use X?" phrases
    // ================================================================

    [Fact]
    public void TryResolve_ResolvesToGetDomainGuidance_ForHowDoIUseTasks()
    {
        var resolved = _resolver.TryResolve("How do I use tasks?", out var action, out var parameters);

        Assert.True(resolved);
        Assert.Equal("GetDomainGuidance", action!.Name);
        Assert.Equal("Tasks",             parameters!["domainName"]);
    }

    // ================================================================
    // Domain alias resolution
    // ================================================================

    [Fact]
    public void TryResolve_ResolvesToGetDomainGuidance_WithTodoAlias()
    {
        var resolved = _resolver.TryResolve("Tell me about todo", out var action, out var parameters);

        Assert.True(resolved);
        Assert.Equal("GetDomainGuidance", action!.Name);
        Assert.Equal("Tasks",             parameters!["domainName"]);
    }

    [Fact]
    public void TryResolve_ResolvesToGetDomainGuidance_WithDiaryAlias()
    {
        var resolved = _resolver.TryResolve("Tell me about diary", out var action, out var parameters);

        Assert.True(resolved);
        Assert.Equal("GetDomainGuidance", action!.Name);
        Assert.Equal("Journal",           parameters!["domainName"]);
    }

    [Fact]
    public void TryResolve_ResolvesToGetDomainGuidance_WithInsightsAlias()
    {
        var resolved = _resolver.TryResolve("Tell me about insights", out var action, out var parameters);

        Assert.True(resolved);
        Assert.Equal("GetDomainGuidance", action!.Name);
        Assert.Equal("Knowledge",         parameters!["domainName"]);
    }

    // ================================================================
    // Non-guidance phrases should NOT route to GetDomainGuidance
    // ================================================================

    [Fact]
    public void TryResolve_DoesNotRouteToGuidance_ForAddTask()
    {
        var resolved = _resolver.TryResolve("Add a task: buy milk", out var action, out _);

        if (resolved)
            Assert.NotEqual("GetDomainGuidance", action!.Name);
    }

    [Fact]
    public void TryResolve_DoesNotRouteToGuidance_ForUnknownDomainPhrase()
    {
        // "Tell me about blorp" — "blorp" does not resolve to any known domain alias
        var resolved = _resolver.TryResolve("Tell me about blorp", out var action, out _);

        if (resolved)
            Assert.NotEqual("GetDomainGuidance", action!.Name);
    }
}
