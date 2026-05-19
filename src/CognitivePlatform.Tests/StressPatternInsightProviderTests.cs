using Moq;
using CognitivePlatform.Api.Conversation;
using CognitivePlatform.Api.Domains.Activity;
using CognitivePlatform.Api.Domains.Journal;
using CognitivePlatform.Api.Domains.Journal.Interfaces;
using CognitivePlatform.Api.Insights;
using CognitivePlatform.Api.Insights.Models;
using CognitivePlatform.Api.Models;
using CognitivePlatform.Api.Registry;
using Microsoft.Extensions.Logging.Abstractions;

namespace CognitivePlatform.Tests;

public class StressPatternInsightProviderTests
{
    private readonly Mock<IJournalService>       _journalServiceMock = new();
    private readonly StressPatternInsightProvider _provider;

    public StressPatternInsightProviderTests()
    {
        _provider = new StressPatternInsightProvider(_journalServiceMock.Object);
    }

    private static ConversationContext MakeContext() => new("test-session");

    private static async Task<List<Insight>> CollectAsync(IAsyncEnumerable<Insight> source)
    {
        var result = new List<Insight>();
        await foreach (var insight in source)
            result.Add(insight);
        return result;
    }

    /// <summary>
    /// Builds a JournalEntryWithRevision whose text contains a stress signal,
    /// anchored to a specific UTC date so distinct-day counting is predictable.
    /// </summary>
    private static JournalEntryWithRevision MakeStressEntry(DateTimeOffset createdUtc, string text = "I felt really stressed today.")
        => new(
            new JournalEntry { Id = Guid.NewGuid().ToString("N"), CreatedUtc = createdUtc }
          , new JournalRevision { RevisionId = Guid.NewGuid().ToString("N"), EntryId = Guid.NewGuid().ToString("N"), Text = text }
          , IsEdited: false);

    private static JournalEntryWithRevision MakeNeutralEntry(DateTimeOffset createdUtc)
        => new(
            new JournalEntry { Id = Guid.NewGuid().ToString("N"), CreatedUtc = createdUtc }
          , new JournalRevision { RevisionId = Guid.NewGuid().ToString("N"), EntryId = Guid.NewGuid().ToString("N"), Text = "Had a productive and calm day." }
          , IsEdited: false);

    // ====================================================================
    // Core logic — emit
    // ====================================================================

    [Fact]
    public async Task GenerateAsync_YieldsInsight_WhenThreeOrMoreDistinctStressDaysInLastWeek()
    {
        var now = DateTimeOffset.UtcNow;

        var entries = new[]
        {
            MakeStressEntry(now.AddDays(-1))
          , MakeStressEntry(now.AddDays(-2))
          , MakeStressEntry(now.AddDays(-3))
        };

        _journalServiceMock
            .Setup(service => service.ListEntries(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>()))
            .Returns(entries);

        var insights = await CollectAsync(_provider.GenerateAsync(MakeContext()));

        var single = Assert.Single(insights);
        Assert.Equal("coaching.stress-pattern-weekly",                                                              single.DeduplicationKey);
        Assert.Equal("You've logged stress signals 3 days this week. Want to explore what's been driving that?",   single.Message);
        Assert.Equal("AddJournalEntry",                                                                             single.SuggestedAction);
        Assert.Equal(InsightCategory.Habit,                                                                         single.Category);
        Assert.Equal(InsightPriority.Normal,                                                                        single.Priority);
        Assert.NotNull(single.Reasoning);
        Assert.Contains("3", single.Reasoning.Explanation);
    }

    // ====================================================================
    // Core logic — suppress
    // ====================================================================

    [Fact]
    public async Task GenerateAsync_YieldsNothing_WhenFewerThanThreeStressDays()
    {
        var now = DateTimeOffset.UtcNow;

        var entries = new[]
        {
            MakeStressEntry(now.AddDays(-1))
          , MakeStressEntry(now.AddDays(-2))
          , MakeNeutralEntry(now.AddDays(-3))
        };

        _journalServiceMock
            .Setup(service => service.ListEntries(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>()))
            .Returns(entries);

        var insights = await CollectAsync(_provider.GenerateAsync(MakeContext()));

        Assert.Empty(insights);
    }

    // ====================================================================
    // Deduplication — verified via InsightEngine (engine owns dedup logic)
    // ====================================================================

    [Fact]
    public async Task InsightEngine_SuppressesStressInsight_WhenRecentlyEmitted()
    {
        var now = DateTimeOffset.UtcNow;

        var entries = new[]
        {
            MakeStressEntry(now.AddDays(-1))
          , MakeStressEntry(now.AddDays(-2))
          , MakeStressEntry(now.AddDays(-3))
        };

        _journalServiceMock
            .Setup(service => service.ListEntries(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>()))
            .Returns(entries);

        var historyMock = new Mock<IInsightHistoryStore>();
        historyMock
            .Setup(store => store.WasRecentlyEmittedAsync(
                "coaching.stress-pattern-weekly"
              , It.IsAny<TimeSpan>()
              , It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Registry must recognise "AddJournalEntry" or the insight is suppressed by action-validation.
        var registryMock = new Mock<IActionRegistry>();
        registryMock
            .Setup(registry => registry.FindByName("AddJournalEntry"))
            .Returns(new CognitivePlatform.Api.Models.ActionMetadata { Name = "AddJournalEntry" });

        var activityMock = new Mock<IActivityLog>();
        activityMock
            .Setup(log => log.LogAsync(It.IsAny<ActivityEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var engine = new InsightEngine(
            new IInsightProvider[] { _provider }
          , registryMock.Object
          , historyMock.Object
          , activityMock.Object
          , new InsightPolicy()
          , NullLogger<InsightEngine>.Instance);

        var results = await engine.GenerateInsightsAsync(MakeContext());

        Assert.Empty(results);
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
