using Moq;
using CognitivePlatform.Api.Conversation;
using CognitivePlatform.Api.Domains.Activity;
using CognitivePlatform.Api.Domains.Journal.Interfaces;
using CognitivePlatform.Api.Insights;
using CognitivePlatform.Api.Insights.Models;
using CognitivePlatform.Api.Models;
using CognitivePlatform.Api.Registry;
using Microsoft.Extensions.Logging.Abstractions;

namespace CognitivePlatform.Tests;

public class JournalActivityInsightProviderTests
{
    private readonly Mock<IJournalService> _journalServiceMock = new();
    private readonly JournalActivityInsightProvider _provider;

    public JournalActivityInsightProviderTests()
    {
        _provider = new JournalActivityInsightProvider(_journalServiceMock.Object);
    }

    private static ConversationContext MakeContext() => new("test-session");

    private void StubNoEntriesToday()
    {
        _journalServiceMock
            .Setup(service => service.ListEntries(It.IsAny<DateTimeOffset?>()
                                                , It.IsAny<DateTimeOffset?>()))
            .Returns(Array.Empty<JournalEntryWithRevision>());
    }

    private void StubEntryExistsToday()
    {
        var fakeEntry = new JournalEntryWithRevision(
            new CognitivePlatform.Api.Domains.Journal.JournalEntry { Id = "abc" }
          , new CognitivePlatform.Api.Domains.Journal.JournalRevision { RevisionId = "rev1", EntryId = "abc", Text = "Today's entry" }
          , IsEdited: false);

        _journalServiceMock
            .Setup(service => service.ListEntries(It.IsAny<DateTimeOffset?>()
                                                , It.IsAny<DateTimeOffset?>()))
            .Returns(new[] { fakeEntry });
    }

    // ====================================================================
    // Core logic
    // ====================================================================

    [Fact]
    public async Task GenerateAsync_YieldsInsight_WhenNoJournalEntryToday()
    {
        StubNoEntriesToday();

        var insights = await CollectAsync(_provider.GenerateAsync(MakeContext()));

        var single = Assert.Single(insights);
        Assert.Equal("journal.no-entry-today",                              single.DeduplicationKey);
        Assert.Equal("You haven't journaled today — want to add an entry?", single.Message);
        Assert.Equal("AddJournalEntry",                                      single.SuggestedAction);
        Assert.Equal(InsightCategory.Journal,                                single.Category);
        Assert.Equal(InsightPriority.Normal,                                 single.Priority);
    }

    [Fact]
    public async Task GenerateAsync_PopulatesReasoning_WhenNoJournalEntryToday()
    {
        StubNoEntriesToday();

        var insights = await CollectAsync(_provider.GenerateAsync(MakeContext()));

        var single = Assert.Single(insights);
        Assert.NotNull(single.Reasoning);
        Assert.Equal("No journal entry found for today.", single.Reasoning.Explanation);
        Assert.Empty(single.Reasoning.Evidence);
    }

    [Fact]
    public async Task GenerateAsync_YieldsNothing_WhenEntryExistsToday()
    {
        StubEntryExistsToday();

        var insights = await CollectAsync(_provider.GenerateAsync(MakeContext()));

        Assert.Empty(insights);
    }

    [Fact]
    public void Category_IsJournal()
    {
        Assert.Equal(InsightCategory.Journal, _provider.Category);
    }

    // ====================================================================
    // Fault isolation regression guard (Phase A engine contract)
    // ====================================================================

    [Fact]
    public async Task InsightEngine_StillRunsOtherProviders_WhenJournalProviderFaults()
    {
        // Arrange: a journal provider that throws and a healthy provider.
        var faultingMock = new Mock<IJournalService>();
        faultingMock
            .Setup(service => service.ListEntries(It.IsAny<DateTimeOffset?>()
                                                , It.IsAny<DateTimeOffset?>()))
            .Throws<InvalidOperationException>();

        var faultingProvider = new JournalActivityInsightProvider(faultingMock.Object);

        var healthyInsight = new Insight
                             {
                                     Message          = "Something else"
                                   , DeduplicationKey = "other.signal"
                                   , Category         = InsightCategory.General
                             };
        var healthyProviderMock = new Mock<IInsightProvider>();
        healthyProviderMock.SetupGet(provider => provider.Category).Returns(InsightCategory.General);
        healthyProviderMock
            .Setup(provider => provider.GenerateAsync(It.IsAny<ConversationContext>()
                                                    , It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnumerable(new[] { healthyInsight }));

        var historyMock = new Mock<IInsightHistoryStore>();
        historyMock
            .Setup(store => store.WasRecentlyEmittedAsync(It.IsAny<string>()
                                                        , It.IsAny<TimeSpan>()
                                                        , It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var registryMock = new Mock<IActionRegistry>();
        registryMock.Setup(registry => registry.FindByName(It.IsAny<string>())).Returns((ActionMetadata?)null);

        var activityMock = new Mock<IActivityLog>();
        activityMock.Setup(log => log.LogAsync(It.IsAny<ActivityEvent>(), It.IsAny<CancellationToken>()))
                    .Returns(Task.CompletedTask);

        var engine = new InsightEngine(
            new IInsightProvider[] { faultingProvider, healthyProviderMock.Object }
          , registryMock.Object
          , historyMock.Object
          , activityMock.Object
          , new InsightPolicy()
          , NullLogger<InsightEngine>.Instance);

        var results = await engine.GenerateInsightsAsync(MakeContext());

        // Faulting provider is skipped; the healthy provider's insight survives.
        Assert.Single(results);
        Assert.Equal("other.signal", results[0].DeduplicationKey);

        activityMock.Verify(log => log.LogAsync(
                                It.Is<ActivityEvent>(activityEvent =>
                                    activityEvent.ActivityType == InsightActivityTypes.ProviderFailed)
                              , It.IsAny<CancellationToken>())
                          , Times.Once);
    }

    // ====================================================================
    // Helpers
    // ====================================================================

    private static async Task<List<Insight>> CollectAsync(IAsyncEnumerable<Insight> source)
    {
        var result = new List<Insight>();
        await foreach (var insight in source)
            result.Add(insight);
        return result;
    }

    private static async IAsyncEnumerable<Insight> ToAsyncEnumerable(IEnumerable<Insight> insights)
    {
        foreach (var insight in insights)
        {
            await Task.CompletedTask;
            yield return insight;
        }
    }
}
