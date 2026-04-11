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
}
