using Moq;
using CognitivePlatform.Api.Conversation;
using CognitivePlatform.Api.Domains.Journal;
using CognitivePlatform.Api.Domains.Journal.Interfaces;
using CognitivePlatform.Api.Insights;
using CognitivePlatform.Api.Insights.Models;
using CognitivePlatform.Api.Integrations.Health;
using CognitivePlatform.Api.Integrations.Health.Models;
using CognitivePlatform.Api.Models;

namespace CognitivePlatform.Tests;

public class HealthCorrelationInsightProviderTests
{
    private readonly Mock<IHealthProvider>               _healthMock  = new();
    private readonly Mock<IJournalService>               _journalMock = new();
    private readonly HealthCorrelationInsightProvider    _provider;

    public HealthCorrelationInsightProviderTests()
    {
        _provider = new HealthCorrelationInsightProvider(_healthMock.Object, _journalMock.Object);
    }

    private static ConversationContext MakeContext() => new("test-session");

    private static async Task<List<Insight>> CollectAsync(IAsyncEnumerable<Insight> source)
    {
        var result = new List<Insight>();
        await foreach (var insight in source)
            result.Add(insight);
        return result;
    }

    private static JournalEntryWithRevision MakeMoodEntry(DateTimeOffset createdUtc, int moodScore)
        => new(
            new JournalEntry { Id = Guid.NewGuid().ToString("N"), CreatedUtc = createdUtc }
          , new JournalRevision
            {
                RevisionId = Guid.NewGuid().ToString("N")
              , EntryId    = Guid.NewGuid().ToString("N")
              , Text       = "Journal entry."
              , MoodScore  = moodScore
            }
          , IsEdited: false);

    private void SetupConnectedSleepCallback(Func<DateTimeOffset, int> minutesFactory)
    {
        _healthMock.SetupGet(provider => provider.IsConnected).Returns(true);
        _healthMock
            .Setup(provider => provider.GetSleepAsync(
                It.IsAny<DateTimeOffset>()
              , It.IsAny<DateTimeOffset>()
              , It.IsAny<CancellationToken>()))
            .Returns((DateTimeOffset from, DateTimeOffset to, CancellationToken _) =>
                Task.FromResult(new SleepResult
                                {
                                    TotalMinutes = minutesFactory(from)
                                  , Sessions     = minutesFactory(from) > 0 ? 1 : 0
                                }));
    }

    // ====================================================================
    // Core logic — emit
    // ====================================================================

    [Fact]
    public async Task GenerateAsync_YieldsInsight_WhenCorrelationIsAtOrAboveThreshold()
    {
        // 10 paired days: 5 high-sleep/high-mood + 5 low-sleep/low-mood → r = 1.0
        var today = DateTimeOffset.UtcNow;
        var utcToday = DateTimeOffset.UtcNow.Date;

        SetupConnectedSleepCallback(from =>
        {
            var daysAgo = (int)(utcToday - from.UtcDateTime.Date).TotalDays;
            return daysAgo switch
            {
                >= 1 and <= 5  => 480
              , >= 6 and <= 10 => 300
              , _              => 0
            };
        });

        var entries = Enumerable.Range(1, 5)
            .Select(offset => MakeMoodEntry(today.AddDays(-offset), moodScore: 8))
            .Concat(Enumerable.Range(6, 5)
                .Select(offset => MakeMoodEntry(today.AddDays(-offset), moodScore: 3)))
            .ToList();

        _journalMock
            .Setup(service => service.ListEntries(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>()))
            .Returns(entries);

        var insights = await CollectAsync(_provider.GenerateAsync(MakeContext()));

        var single = Assert.Single(insights);
        Assert.Equal("health.correlation",  single.DeduplicationKey);
        Assert.Equal(InsightCategory.Health, single.Category);
        Assert.Equal(InsightPriority.Normal, single.Priority);
        Assert.NotNull(single.Reasoning);
        Assert.Contains("r =",              single.Reasoning.Explanation);
        Assert.Contains("10",               single.Reasoning.Explanation);
    }

    // ====================================================================
    // Core logic — suppress: too few data points
    // ====================================================================

    [Fact]
    public async Task GenerateAsync_YieldsNothing_WhenFewerThanFiveDataPoints()
    {
        var today    = DateTimeOffset.UtcNow;
        var utcToday = DateTimeOffset.UtcNow.Date;

        // Sleep only for 4 days
        SetupConnectedSleepCallback(from =>
        {
            var daysAgo = (int)(utcToday - from.UtcDateTime.Date).TotalDays;
            return daysAgo is >= 1 and <= 4 ? 480 : 0;
        });

        var entries = Enumerable.Range(1, 4)
            .Select(offset => MakeMoodEntry(today.AddDays(-offset), moodScore: 8))
            .ToList();

        _journalMock
            .Setup(service => service.ListEntries(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>()))
            .Returns(entries);

        var insights = await CollectAsync(_provider.GenerateAsync(MakeContext()));

        Assert.Empty(insights);
    }

    // ====================================================================
    // Graceful degrade — health provider disconnected
    // ====================================================================

    [Fact]
    public async Task GenerateAsync_YieldsNothing_WhenHealthProviderIsDisconnected()
    {
        _healthMock.SetupGet(provider => provider.IsConnected).Returns(false);
        _journalMock
            .Setup(service => service.ListEntries(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>()))
            .Returns(Array.Empty<JournalEntryWithRevision>());

        var insights = await CollectAsync(_provider.GenerateAsync(MakeContext()));

        Assert.Empty(insights);
        _healthMock.Verify(provider => provider.GetSleepAsync(
                It.IsAny<DateTimeOffset>()
              , It.IsAny<DateTimeOffset>()
              , It.IsAny<CancellationToken>())
            , Times.Never);
    }

    // ====================================================================
    // Core logic — suppress: correlation below threshold
    // ====================================================================

    [Fact]
    public async Task GenerateAsync_YieldsNothing_WhenCorrelationIsBelowThreshold()
    {
        // Constant sleep duration → zero variance → Pearson r = 0
        var today    = DateTimeOffset.UtcNow;
        var utcToday = DateTimeOffset.UtcNow.Date;

        SetupConnectedSleepCallback(from =>
        {
            var daysAgo = (int)(utcToday - from.UtcDateTime.Date).TotalDays;
            return daysAgo is >= 1 and <= 7 ? 420 : 0;
        });

        // Mood alternates independently — no correlation with constant sleep
        var entries = Enumerable.Range(1, 7)
            .Select(offset => MakeMoodEntry(today.AddDays(-offset), moodScore: offset % 2 == 0 ? 8 : 3))
            .ToList();

        _journalMock
            .Setup(service => service.ListEntries(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>()))
            .Returns(entries);

        var insights = await CollectAsync(_provider.GenerateAsync(MakeContext()));

        Assert.Empty(insights);
    }

    // ====================================================================
    // Deduplication key
    // ====================================================================

    [Fact]
    public async Task GenerateAsync_DeduplicationKey_IsHealthCorrelation()
    {
        var today    = DateTimeOffset.UtcNow;
        var utcToday = DateTimeOffset.UtcNow.Date;

        SetupConnectedSleepCallback(from =>
        {
            var daysAgo = (int)(utcToday - from.UtcDateTime.Date).TotalDays;
            return daysAgo switch
            {
                >= 1 and <= 5  => 480
              , >= 6 and <= 10 => 300
              , _              => 0
            };
        });

        var entries = Enumerable.Range(1, 5)
            .Select(offset => MakeMoodEntry(today.AddDays(-offset), moodScore: 8))
            .Concat(Enumerable.Range(6, 5)
                .Select(offset => MakeMoodEntry(today.AddDays(-offset), moodScore: 3)))
            .ToList();

        _journalMock
            .Setup(service => service.ListEntries(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>()))
            .Returns(entries);

        var insights = await CollectAsync(_provider.GenerateAsync(MakeContext()));

        var insight = Assert.Single(insights);
        Assert.Equal("health.correlation", insight.DeduplicationKey);
    }

    // ====================================================================
    // Category property
    // ====================================================================

    [Fact]
    public void Category_IsHealth()
    {
        Assert.Equal(InsightCategory.Health, _provider.Category);
    }
}
