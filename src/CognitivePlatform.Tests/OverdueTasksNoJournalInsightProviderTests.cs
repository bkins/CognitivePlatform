using Moq;
using CognitivePlatform.Api.Conversation;
using CognitivePlatform.Api.Domains.Journal;
using CognitivePlatform.Api.Domains.Journal.Interfaces;
using CognitivePlatform.Api.Domains.Tasks;
using CognitivePlatform.Api.Insights;
using CognitivePlatform.Api.Insights.Models;
using CognitivePlatform.Api.Models;

namespace CognitivePlatform.Tests;

public class OverdueTasksNoJournalInsightProviderTests
{
    private readonly Mock<ITaskService>    _taskServiceMock    = new();
    private readonly Mock<IJournalService> _journalServiceMock = new();
    private readonly OverdueTasksNoJournalInsightProvider _provider;

    public OverdueTasksNoJournalInsightProviderTests()
    {
        _provider = new OverdueTasksNoJournalInsightProvider(
            _taskServiceMock.Object
          , _journalServiceMock.Object);
    }

    private static ConversationContext MakeContext() => new("test-session");

    private static async Task<List<Insight>> CollectAsync(IAsyncEnumerable<Insight> source)
    {
        var result = new List<Insight>();
        await foreach (var insight in source)
            result.Add(insight);
        return result;
    }

    private static TaskItem MakeOverdueTask()
        => new()
           {
               ShortDescription = "Overdue task"
             , DueDate          = DateTimeOffset.UtcNow.AddDays(-2)
           };

    private static TaskItem MakeFutureTask()
        => new()
           {
               ShortDescription = "Not yet due"
             , DueDate          = DateTimeOffset.UtcNow.AddDays(3)
           };

    private void StubActiveTasks(IEnumerable<TaskItem> tasks)
        => _taskServiceMock
               .Setup(service => service.GetActive())
               .Returns(tasks.ToList());

    private void StubNoJournalToday()
        => _journalServiceMock
               .Setup(service => service.ListEntries(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>()))
               .Returns(Array.Empty<JournalEntryWithRevision>());

    private void StubJournalEntryToday()
    {
        var entry = new JournalEntryWithRevision(
            new JournalEntry { Id = "e1", CreatedUtc = DateTimeOffset.UtcNow }
          , new JournalRevision { RevisionId = "r1", EntryId = "e1", Text = "Today's entry." }
          , IsEdited: false);

        _journalServiceMock
            .Setup(service => service.ListEntries(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>()))
            .Returns(new[] { entry });
    }

    // ====================================================================
    // Core logic — emit
    // ====================================================================

    [Fact]
    public async Task GenerateAsync_YieldsInsight_WhenTwoOrMoreOverdueTasksAndNoJournalToday()
    {
        StubActiveTasks(new[] { MakeOverdueTask(), MakeOverdueTask() });
        StubNoJournalToday();

        var insights = await CollectAsync(_provider.GenerateAsync(MakeContext()));

        var single = Assert.Single(insights);
        Assert.Equal("coaching.overdue-no-journal",                                                                                         single.DeduplicationKey);
        Assert.Equal("You have 2 overdue tasks and haven't journaled today. It might help to write down what's blocking you.",              single.Message);
        Assert.Equal("AddJournalEntry",                                                                                                     single.SuggestedAction);
        Assert.Equal(InsightCategory.Tasks,                                                                                                 single.Category);
        Assert.Equal(InsightPriority.Normal,                                                                                                single.Priority);
        Assert.NotNull(single.Reasoning);
        Assert.Contains("2", single.Reasoning.Explanation);
    }

    // ====================================================================
    // Core logic — suppress: journal written today
    // ====================================================================

    [Fact]
    public async Task GenerateAsync_YieldsNothing_WhenOverdueTasksExistButJournalWasWrittenToday()
    {
        StubActiveTasks(new[] { MakeOverdueTask(), MakeOverdueTask() });
        StubJournalEntryToday();

        var insights = await CollectAsync(_provider.GenerateAsync(MakeContext()));

        Assert.Empty(insights);
    }

    // ====================================================================
    // Core logic — suppress: not enough overdue tasks
    // ====================================================================

    [Fact]
    public async Task GenerateAsync_YieldsNothing_WhenFewerThanTwoOverdueTasks()
    {
        // One overdue task + one future task = only 1 overdue, below threshold.
        StubActiveTasks(new[] { MakeOverdueTask(), MakeFutureTask() });
        StubNoJournalToday();

        var insights = await CollectAsync(_provider.GenerateAsync(MakeContext()));

        Assert.Empty(insights);
    }

    // ====================================================================
    // Category property
    // ====================================================================

    [Fact]
    public void Category_IsTasks()
    {
        Assert.Equal(InsightCategory.Tasks, _provider.Category);
    }
}
