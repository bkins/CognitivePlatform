using CognitivePlatform.Api.Actions;
using CognitivePlatform.Api.Models;
using CognitivePlatform.Api.Registry.Domains;

namespace CognitivePlatform.Tests;

public class GuidanceActionsTests
{
    // ================================================================
    // Helpers
    // ================================================================

    private static ActionMetadata MakeAction(
        string name
      , string description
      , IDomainDefinition? domain
      , string[] examples)
        => new()
           {
               Name        = name
             , Description = description
             , Domain      = domain
             , Parameters  = new List<ParameterMetadata>()
             , Examples    = examples
           };

    private static readonly IDomainDefinition TasksDomainInstance    = new TasksDomain();
    private static readonly IDomainDefinition JournalDomainInstance  = new JournalDomain();
    private static readonly IDomainDefinition SystemDomainInstance   = new SystemDomain();
    private static readonly IDomainDefinition CalendarDomainInstance = new CalendarDomain();

    private static readonly List<ActionMetadata> SampleActions = new()
    {
            MakeAction("AddTask",    "Adds a new task to the list.",     TasksDomainInstance,    ["Add a task: buy milk"])
          , MakeAction("ListTasks",  "Lists tasks with optional filters.", TasksDomainInstance,  ["Show my tasks", "List urgent tasks"])
          , MakeAction("AddJournalEntry", "Adds a journal entry.",        JournalDomainInstance, ["Journal: Had a good day"])
          , MakeAction("ListActions", "Lists all registered actions.",    SystemDomainInstance,  ["What can you do?"])
    };

    // ================================================================
    // BuildGuidanceResponse — known domain
    // ================================================================

    [Fact]
    public void BuildGuidanceResponse_IncludesDomainName_InHeader()
    {
        var result = GuidanceActions.BuildGuidanceResponse("Tasks", SampleActions);

        Assert.Contains("Tasks", result);
    }

    [Fact]
    public void BuildGuidanceResponse_IncludesDomainActions_ForMatchingDomain()
    {
        var result = GuidanceActions.BuildGuidanceResponse("Tasks", SampleActions);

        Assert.Contains("AddTask",   result);
        Assert.Contains("ListTasks", result);
    }

    [Fact]
    public void BuildGuidanceResponse_ExcludesOtherDomainActions()
    {
        var result = GuidanceActions.BuildGuidanceResponse("Tasks", SampleActions);

        Assert.DoesNotContain("AddJournalEntry", result);
    }

    [Fact]
    public void BuildGuidanceResponse_IncludesActionExamples()
    {
        var result = GuidanceActions.BuildGuidanceResponse("Tasks", SampleActions);

        Assert.Contains("Add a task: buy milk", result);
    }

    [Fact]
    public void BuildGuidanceResponse_IncludesDomainDescription()
    {
        var result = GuidanceActions.BuildGuidanceResponse("Tasks", SampleActions);

        Assert.Contains("to-do list", result);
        Assert.Contains("Eisenhower", result);
    }

    [Fact]
    public void BuildGuidanceResponse_IncludesRelationshipText_ForTasksDomain()
    {
        var result = GuidanceActions.BuildGuidanceResponse("Tasks", SampleActions);

        Assert.Contains("Insight Engine", result);
    }

    // ================================================================
    // BuildGuidanceResponse — domain alias resolution
    // ================================================================

    [Fact]
    public void BuildGuidanceResponse_ResolvesAlias_TaskLowercase()
    {
        var result = GuidanceActions.BuildGuidanceResponse("task", SampleActions);

        Assert.Contains("Tasks", result);
        Assert.Contains("AddTask", result);
    }

    [Fact]
    public void BuildGuidanceResponse_ResolvesAlias_JournalLowercase()
    {
        var result = GuidanceActions.BuildGuidanceResponse("journal", SampleActions);

        Assert.Contains("Journal",         result);
        Assert.Contains("AddJournalEntry", result);
    }

    [Fact]
    public void BuildGuidanceResponse_IsCaseInsensitive_ForDomainName()
    {
        var result = GuidanceActions.BuildGuidanceResponse("TASKS", SampleActions);

        Assert.Contains("AddTask", result);
    }

    // ================================================================
    // BuildGuidanceResponse — unknown domain
    // ================================================================

    [Fact]
    public void BuildGuidanceResponse_ReturnsNotRecognized_ForUnknownDomain()
    {
        var result = GuidanceActions.BuildGuidanceResponse("Blorp", SampleActions);

        Assert.Contains("don't recognize", result);
        Assert.Contains("Blorp",           result);
    }

    [Fact]
    public void BuildGuidanceResponse_ListsKnownAreas_WhenDomainUnknown()
    {
        var result = GuidanceActions.BuildGuidanceResponse("xyz", SampleActions);

        Assert.Contains("Tasks",    result);
        Assert.Contains("Journal",  result);
        Assert.Contains("Calendar", result);
    }

    // ================================================================
    // BuildGuidanceResponse — empty / null-equivalent domain name
    // ================================================================

    [Fact]
    public void BuildGuidanceResponse_WithEmptyDomainName_StillResolvesUnknown()
    {
        var result = GuidanceActions.BuildGuidanceResponse(string.Empty, SampleActions);

        Assert.Contains("don't recognize", result);
    }

    // ================================================================
    // BuildGuidanceResponse — Journal domain includes relationship text
    // ================================================================

    [Fact]
    public void BuildGuidanceResponse_IncludesRelationshipText_ForJournalDomain()
    {
        var result = GuidanceActions.BuildGuidanceResponse("Journal", SampleActions);

        Assert.Contains("Insight Engine", result);
    }

    // ================================================================
    // BuildGuidanceResponse — Calendar domain (no actions in sample)
    // ================================================================

    [Fact]
    public void BuildGuidanceResponse_WithNoActionsInDomain_StillReturnsGuidance()
    {
        var result = GuidanceActions.BuildGuidanceResponse("Calendar", SampleActions);

        // Description should still be present even when no actions match
        Assert.Contains("Calendar", result);
        Assert.Contains("Google Calendar", result);
    }

    // ================================================================
    // BuildGuidanceResponse — partial alias prefix match
    // ================================================================

    [Fact]
    public void BuildGuidanceResponse_ResolvesPartialAlias_TaskManagement()
    {
        var result = GuidanceActions.BuildGuidanceResponse("task management", SampleActions);

        Assert.Contains("Tasks", result);
    }
}
