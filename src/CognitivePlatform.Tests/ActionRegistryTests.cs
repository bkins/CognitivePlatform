using CognitivePlatform.Api.Registry;

namespace CognitivePlatform.Tests;

/// <summary>
/// Verifies that ActionRegistry correctly detects [DestructiveAction] attributes.
/// </summary>
public class ActionRegistryTests
{
    private readonly ActionRegistry _registry = new();

    // ================================================================
    // DESTRUCTIVE DETECTION — known destructive actions
    // ================================================================

    [Fact]
    public void DeleteTask_IsMarkedDestructive()
    {
        var action = _registry.FindByName("DeleteTask");

        Assert.NotNull(action);
        Assert.True(action.IsDestructive);
    }

    [Fact]
    public void DeleteJournalEntry_IsMarkedDestructive()
    {
        var action = _registry.FindByName("DeleteJournalEntry");

        Assert.NotNull(action);
        Assert.True(action.IsDestructive);
    }

    // ================================================================
    // DESTRUCTIVE DETECTION — non-destructive actions must be false
    // ================================================================

    [Fact]
    public void ListTasks_IsNotDestructive()
    {
        var action = _registry.FindByName("ListTasks");

        Assert.NotNull(action);
        Assert.False(action.IsDestructive);
    }

    [Fact]
    public void AddTask_IsNotDestructive()
    {
        var action = _registry.FindByName("AddTask");

        Assert.NotNull(action);
        Assert.False(action.IsDestructive);
    }

    [Fact]
    public void AddJournalEntry_IsNotDestructive()
    {
        var action = _registry.FindByName("AddJournalEntry");

        Assert.NotNull(action);
        Assert.False(action.IsDestructive);
    }

    // ================================================================
    // IsReplayable — state-mutating actions are replayable
    // ================================================================

    [Theory]
    [InlineData("AddTask")]
    [InlineData("CompleteTask")]
    [InlineData("DeleteTask")]
    [InlineData("UpdateTaskDueDate")]
    [InlineData("AddJournalEntry")]
    [InlineData("DeleteJournalEntry")]
    [InlineData("LogActivity")]
    public void StateMutatingAction_IsMarkedReplayable(string actionName)
    {
        var action = _registry.FindByName(actionName);

        Assert.NotNull(action);
        Assert.True(action.IsReplayable, $"{actionName} should be marked IsReplayable = true");
    }

    // ================================================================
    // IsReplayable — read-only actions must NOT be replayable
    // ================================================================

    [Theory]
    [InlineData("ListTasks")]
    [InlineData("ListJournalEntries")]
    [InlineData("ListActivities")]
    public void ReadOnlyAction_IsNotReplayable(string actionName)
    {
        var action = _registry.FindByName(actionName);

        Assert.NotNull(action);
        Assert.False(action.IsReplayable, $"{actionName} should not be replayable");
    }
}
