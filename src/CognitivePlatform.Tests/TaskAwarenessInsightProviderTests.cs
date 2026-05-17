using Moq;
using CognitivePlatform.Api.Conversation;
using CognitivePlatform.Api.Domains.Tasks;
using CognitivePlatform.Api.Insights;
using CognitivePlatform.Api.Insights.Models;

namespace CognitivePlatform.Tests;

public class TaskAwarenessInsightProviderTests
{
    private readonly Mock<ITaskService>         _taskServiceMock = new();
    private readonly TaskAwarenessInsightProvider _provider;

    public TaskAwarenessInsightProviderTests()
    {
        _provider = new TaskAwarenessInsightProvider(_taskServiceMock.Object);
    }

    private static ConversationContext MakeContext() => new("test-session");

    private void StubOpenTasks(int count)
    {
        var tasks = Enumerable.Range(0, count)
            .Select(_ => new TaskItem { ShortDescription = "A task" })
            .ToList();

        _taskServiceMock
            .Setup(service => service.GetActive())
            .Returns(tasks);
    }

    private static async Task<List<Insight>> CollectAsync(IAsyncEnumerable<Insight> source)
    {
        var result = new List<Insight>();
        await foreach (var insight in source)
            result.Add(insight);
        return result;
    }

    // ====================================================================
    // Core logic
    // ====================================================================

    [Fact]
    public async Task GenerateAsync_YieldsInsight_WhenOpenTaskCountExceedsThreshold()
    {
        StubOpenTasks(4);

        var insights = await CollectAsync(_provider.GenerateAsync(MakeContext()));

        var single = Assert.Single(insights);
        Assert.Equal("tasks.open-tasks-reminder",                    single.DeduplicationKey);
        Assert.Equal("You have 4 open tasks. Want help prioritising?", single.Message);
        Assert.Equal("ListTasksSummary",                              single.SuggestedAction);
        Assert.Equal(InsightCategory.Tasks,                           single.Category);
        Assert.Equal(InsightPriority.Normal,                          single.Priority);
    }

    [Fact]
    public async Task GenerateAsync_YieldsNothing_WhenOpenTaskCountBelowThreshold()
    {
        StubOpenTasks(2);

        var insights = await CollectAsync(_provider.GenerateAsync(MakeContext()));

        Assert.Empty(insights);
    }

    [Fact]
    public async Task GenerateAsync_YieldsNothing_WhenOpenTaskCountEqualsThreshold()
    {
        StubOpenTasks(3);

        var insights = await CollectAsync(_provider.GenerateAsync(MakeContext()));

        Assert.Empty(insights);
    }

    [Fact]
    public void Category_IsTasks()
    {
        Assert.Equal(InsightCategory.Tasks, _provider.Category);
    }
}
