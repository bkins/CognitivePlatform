using Moq;
using CognitivePlatform.Api.Conversation;
using CognitivePlatform.Api.Domains.Journal;
using CognitivePlatform.Api.Domains.Journal.Interfaces;
using CognitivePlatform.Api.Domains.Tasks;
using CognitivePlatform.Api.Insights;
using CognitivePlatform.Api.Insights.Models;
using CognitivePlatform.Api.Integrations.Health;
using CognitivePlatform.Api.Integrations.Health.Models;
using CognitivePlatform.Api.Models;

namespace CognitivePlatform.Tests;

public class HabitReinforcementInsightProviderTests
{
    private readonly Mock<IJournalService>               _journalMock = new();
    private readonly Mock<ITaskService>                  _taskMock    = new();
    private readonly Mock<IHealthProvider>               _healthMock  = new();
    private readonly HabitReinforcementInsightProvider   _provider;

    public HabitReinforcementInsightProviderTests()
    {
        _provider = new HabitReinforcementInsightProvider(
            _journalMock.Object
          , _taskMock.Object
          , _healthMock.Object);
    }

    private static ConversationContext MakeContext() => new("test-session");

    private static async Task<List<Insight>> CollectAsync(IAsyncEnumerable<Insight> source)
    {
        var result = new List<Insight>();
        await foreach (var insight in source)
            result.Add(insight);
        return result;
    }

    private static JournalEntryWithRevision MakeEntry(DateTimeOffset createdUtc)
        => new(
            new JournalEntry { Id = Guid.NewGuid().ToString("N"), CreatedUtc = createdUtc }
          , new JournalRevision
            {
                RevisionId = Guid.NewGuid().ToString("N")
              , EntryId    = Guid.NewGuid().ToString("N")
              , Text       = "Entry."
            }
          , IsEdited: false);

    private static TaskItem MakeCompletedTask(DateTimeOffset completedAt)
        => new() { ShortDescription = "Task", CompletedAt = completedAt };

    private void SetupDisconnectedHealth()
        => _healthMock.SetupGet(p => p.IsConnected).Returns(false);

    private void SetupConnectedSleepCallback(Func<DateTimeOffset, int> minutesFactory)
    {
        _healthMock.SetupGet(p => p.IsConnected).Returns(true);
        _healthMock
            .Setup(p => p.GetSleepAsync(
                It.IsAny<DateTimeOffset>()
              , It.IsAny<DateTimeOffset>()
              , It.IsAny<CancellationToken>()))
            .Returns((DateTimeOffset from, DateTimeOffset to, CancellationToken _) =>
                Task.FromResult(new SleepResult { TotalMinutes = minutesFactory(from) }));
    }

    // ====================================================================
    // Signal 1 — Journaling streak: fires at day 5
    // ====================================================================

    [Fact]
    public async Task GenerateAsync_YieldsStreakInsight_WhenStreakIsFiveOrMoreConsecutiveDays()
    {
        var now = DateTimeOffset.UtcNow;
        // 5 consecutive local days including today
        var entries = Enumerable.Range(0, 5)
            .Select(offset => MakeEntry(now.AddDays(-offset)))
            .ToList();

        _journalMock
            .Setup(service => service.ListEntries(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>()))
            .Returns(entries);

        _taskMock.Setup(service => service.GetCompleted()).Returns([]);
        SetupDisconnectedHealth();

        var insights = await CollectAsync(_provider.GenerateAsync(MakeContext()));

        var single = Assert.Single(insights);
        Assert.Equal("habit.journaling-streak", single.DeduplicationKey);
        Assert.Equal(InsightCategory.Habit,     single.Category);
        Assert.Contains("5",                    single.Message);
        Assert.NotNull(single.Reasoning);
        Assert.Contains("streak",               single.Reasoning.Explanation.ToLower());
    }

    [Fact]
    public async Task GenerateAsync_YieldsNothing_WhenJournalingStreakIsBelowThreshold()
    {
        var now = DateTimeOffset.UtcNow;
        // Only 4 consecutive days — below threshold of 5
        var entries = Enumerable.Range(0, 4)
            .Select(offset => MakeEntry(now.AddDays(-offset)))
            .ToList();

        _journalMock
            .Setup(service => service.ListEntries(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>()))
            .Returns(entries);

        _taskMock.Setup(service => service.GetCompleted()).Returns([]);
        SetupDisconnectedHealth();

        var insights = await CollectAsync(_provider.GenerateAsync(MakeContext()));

        Assert.Empty(insights);
    }

    // ====================================================================
    // Signal 2 — Task completion rate: fires at ≥ 20% improvement
    // ====================================================================

    [Fact]
    public async Task GenerateAsync_YieldsCompletionInsight_WhenThisWeekRateImproves20Percent()
    {
        var now = DateTimeOffset.UtcNow;

        // 5 this week, 3 last week → ~67% improvement
        var completed = new List<TaskItem>
        {
            MakeCompletedTask(now.AddDays(-1))
          , MakeCompletedTask(now.AddDays(-2))
          , MakeCompletedTask(now.AddDays(-3))
          , MakeCompletedTask(now.AddDays(-4))
          , MakeCompletedTask(now.AddDays(-5))
          , MakeCompletedTask(now.AddDays(-8))
          , MakeCompletedTask(now.AddDays(-9))
          , MakeCompletedTask(now.AddDays(-10))
        };

        _journalMock
            .Setup(service => service.ListEntries(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>()))
            .Returns(Array.Empty<JournalEntryWithRevision>());

        _taskMock.Setup(service => service.GetCompleted()).Returns(completed);
        SetupDisconnectedHealth();

        var insights = await CollectAsync(_provider.GenerateAsync(MakeContext()));

        var single = Assert.Single(insights);
        Assert.Equal("habit.task-completion-rate", single.DeduplicationKey);
        Assert.Equal(InsightCategory.Habit,        single.Category);
        Assert.NotNull(single.Reasoning);
        Assert.Contains("5",                       single.Reasoning.Explanation);
    }

    // ====================================================================
    // Signal 3 — Sleep consistency: fires when σ < 30 min
    // ====================================================================

    [Fact]
    public async Task GenerateAsync_YieldsSleepInsight_WhenSleepDurationIsConsistent()
    {
        // Sleep at 7h ± 10 min every day → very consistent
        var utcToday = DateTimeOffset.UtcNow.Date;

        _journalMock
            .Setup(service => service.ListEntries(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>()))
            .Returns(Array.Empty<JournalEntryWithRevision>());

        _taskMock.Setup(service => service.GetCompleted()).Returns([]);

        SetupConnectedSleepCallback(from =>
        {
            var daysAgo = (int)(utcToday - from.UtcDateTime.Date).TotalDays;
            // Tiny variation: alternate 420 ± 5 min
            return daysAgo is >= 1 and <= 7 ? (420 + (daysAgo % 2 == 0 ? 5 : -5)) : 0;
        });

        var insights = await CollectAsync(_provider.GenerateAsync(MakeContext()));

        var single = Assert.Single(insights);
        Assert.Equal("habit.sleep-consistency", single.DeduplicationKey);
        Assert.Equal(InsightCategory.Habit,     single.Category);
    }

    // ====================================================================
    // Single signal per turn cap
    // ====================================================================

    [Fact]
    public async Task GenerateAsync_YieldsAtMostOneInsight_WhenMultipleSignalsAreActive()
    {
        var now = DateTimeOffset.UtcNow;

        // All three signals active:
        // 1. Journaling streak ≥ 5
        var entries = Enumerable.Range(0, 7)
            .Select(offset => MakeEntry(now.AddDays(-offset)))
            .ToList();
        _journalMock
            .Setup(service => service.ListEntries(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>()))
            .Returns(entries);

        // 2. Task completion rate improved
        var completed = Enumerable.Range(1, 5).Select(offset => MakeCompletedTask(now.AddDays(-offset)))
            .Concat(Enumerable.Range(8, 1).Select(offset => MakeCompletedTask(now.AddDays(-offset))))
            .ToList();
        _taskMock.Setup(service => service.GetCompleted()).Returns(completed);

        // 3. Sleep consistent
        var utcToday = DateTimeOffset.UtcNow.Date;
        SetupConnectedSleepCallback(from =>
        {
            var daysAgo = (int)(utcToday - from.UtcDateTime.Date).TotalDays;
            return daysAgo is >= 1 and <= 7 ? 420 : 0;
        });

        var insights = await CollectAsync(_provider.GenerateAsync(MakeContext()));

        Assert.Single(insights);
    }

    // ====================================================================
    // Category property
    // ====================================================================

    [Fact]
    public void Category_IsHabit()
    {
        Assert.Equal(InsightCategory.Habit, _provider.Category);
    }
}
