using Moq;
using CognitivePlatform.Api.Conversation;
using CognitivePlatform.Api.Domains.Activity;
using CognitivePlatform.Api.Insights;
using CognitivePlatform.Api.Insights.Models;
using CognitivePlatform.Api.Models;
using CognitivePlatform.Api.Registry;
using Microsoft.Extensions.Logging.Abstractions;

namespace CognitivePlatform.Tests;

public class InsightEngineTests
{
    private readonly Mock<IActionRegistry>      _registryMock     = new();
    private readonly Mock<IInsightHistoryStore> _historyStoreMock = new();
    private readonly Mock<IActivityLog>         _activityLogMock  = new();
    private readonly InsightPolicy              _policy           = new();

    public InsightEngineTests()
    {
        _historyStoreMock
            .Setup(store => store.WasRecentlyEmittedAsync(It.IsAny<string>()
                                                        , It.IsAny<TimeSpan>()
                                                        , It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _registryMock.Setup(reg => reg.FindByName(It.IsAny<string>())).Returns((ActionMetadata?)null);
    }

    // MaxPerTurn is 2 in the default InsightPolicy. The cap test relies on this value.
    // If the default changes, update the [InlineData] in the cap test below.
    private const int DefaultMaxPerTurn = 2;

    private InsightEngine BuildEngine(params IInsightProvider[] providers) =>
        new(providers
          , _registryMock.Object
          , _historyStoreMock.Object
          , _activityLogMock.Object
          , _policy
          , NullLogger<InsightEngine>.Instance);

    private static ConversationContext MakeContext(string? lastMessage = null) =>
        new("test-session") { LastUserMessage = lastMessage };

    // ================================================================
    // FAULT ISOLATION
    // ================================================================

    [Fact]
    public async Task GenerateInsightsAsync_SkipsFaultingProvider_OtherProvidersStillRun()
    {
        var faultingMock = new Mock<IInsightProvider>();
        faultingMock.SetupGet(provider => provider.Category).Returns(InsightCategory.General);
        faultingMock.Setup(provider => provider.GenerateAsync(It.IsAny<ConversationContext>()
                                                            , It.IsAny<CancellationToken>()))
                    .Returns(FaultingAsync());

        var healthy        = MakeInsight("healthy.signal");
        var healthyMock    = MakeProvider(InsightCategory.Tasks, healthy);

        var engine = BuildEngine(faultingMock.Object, healthyMock.Object);
        var result = await engine.GenerateInsightsAsync(MakeContext());

        Assert.Single(result);
        Assert.Equal("healthy.signal", result[0].DeduplicationKey);

        _activityLogMock.Verify(log => log.LogAsync(
                                    It.Is<ActivityEvent>(activityEvent => activityEvent.ActivityType == InsightActivityTypes.ProviderFailed)
                                  , It.IsAny<CancellationToken>())
                              , Times.Once);
    }

    // ================================================================
    // DEDUPLICATION
    // ================================================================

    [Fact]
    public async Task GenerateInsightsAsync_EmitsOnce_WhenSameDeduplicationKeyFromTwoProviders()
    {
        const string sharedKey = "shared.dedup.key";
        var insightA = MakeInsight(sharedKey, priority: InsightPriority.Normal);
        var insightB = MakeInsight(sharedKey, priority: InsightPriority.High);

        var engine = BuildEngine(MakeProvider(InsightCategory.General, insightA).Object
                               , MakeProvider(InsightCategory.Tasks,   insightB).Object);

        var result = await engine.GenerateInsightsAsync(MakeContext());

        Assert.Single(result);
        Assert.Equal(sharedKey, result[0].DeduplicationKey);
    }

    [Fact]
    public async Task GenerateInsightsAsync_DropsInsight_AndLogsDeduplicated_WhenHistoryHasRecentEntry()
    {
        const string seenKey  = "seen.before";
        const string freshKey = "first.time";

        _historyStoreMock
            .Setup(store => store.WasRecentlyEmittedAsync(seenKey
                                                        , It.IsAny<TimeSpan>()
                                                        , It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var seen  = MakeInsight(seenKey);
        var fresh = MakeInsight(freshKey);

        var engine = BuildEngine(MakeProvider(InsightCategory.General, seen, fresh).Object);
        var result = await engine.GenerateInsightsAsync(MakeContext());

        Assert.Single(result);
        Assert.Equal(freshKey, result[0].DeduplicationKey);

        _activityLogMock.Verify(log => log.LogAsync(
                                    It.Is<ActivityEvent>(activityEvent =>
                                          activityEvent.ActivityType == InsightActivityTypes.Deduplicated
                                       && activityEvent.Notes        == seenKey)
                                  , It.IsAny<CancellationToken>())
                              , Times.Once);
    }

    // ================================================================
    // CAP AT MaxPerTurn
    // ================================================================

    [Fact]
    public async Task GenerateInsightsAsync_CapsResults_AtMaxPerTurn_HighestPriorityWins()
    {
        Assert.Equal(DefaultMaxPerTurn, _policy.MaxPerTurn);

        var low    = MakeInsight("low.key",    priority: InsightPriority.Low);
        var normal = MakeInsight("normal.key", priority: InsightPriority.Normal);
        var high   = MakeInsight("high.key",   priority: InsightPriority.High);

        var engine = BuildEngine(MakeProvider(InsightCategory.General, low, normal, high).Object);
        var result = await engine.GenerateInsightsAsync(MakeContext());

        Assert.Equal(DefaultMaxPerTurn, result.Count);
        Assert.Contains(result,         insight => insight.DeduplicationKey == "high.key");
        Assert.Contains(result,         insight => insight.DeduplicationKey == "normal.key");
        Assert.DoesNotContain(result,   insight => insight.DeduplicationKey == "low.key");
    }

    // ================================================================
    // ACTION VALIDATION
    // ================================================================

    [Fact]
    public async Task GenerateInsightsAsync_SuppressesInsight_WhenSuggestedActionNotInRegistry()
    {
        var invalid = new Insight
                      {
                              Message          = "Do something."
                            , SuggestedAction  = "NonExistentAction"
                            , DeduplicationKey = "invalid.action"
                            , Priority         = InsightPriority.High
                      };
        var valid = MakeInsight("valid.no.action");

        // Registry returns null for any name — the invalid one fails validation;
        // the valid one has SuggestedAction == null so it bypasses validation entirely.
        var engine = BuildEngine(MakeProvider(InsightCategory.General, invalid, valid).Object);
        var result = await engine.GenerateInsightsAsync(MakeContext());

        Assert.Single(result);
        Assert.Equal("valid.no.action", result[0].DeduplicationKey);
    }

    // ================================================================
    // ACTIVITY LOG — emission
    // ================================================================

    [Fact]
    public async Task GenerateInsightsAsync_LogsEmittedEvent_PerSurvivingInsight()
    {
        var first  = MakeInsight("first.key");
        var second = MakeInsight("second.key");

        var engine = BuildEngine(MakeProvider(InsightCategory.General, first, second).Object);
        var result = await engine.GenerateInsightsAsync(MakeContext());

        Assert.Equal(2, result.Count);

        _activityLogMock.Verify(log => log.LogAsync(
                                    It.Is<ActivityEvent>(activityEvent =>
                                          activityEvent.ActivityType == InsightActivityTypes.Emitted)
                                  , It.IsAny<CancellationToken>())
                              , Times.Exactly(2));
    }

    // ================================================================
    // HELPERS
    // ================================================================

    private static Insight MakeInsight( string          deduplicationKey
                                      , InsightPriority priority = InsightPriority.Normal ) =>
        new()
        {
                Message          = $"Insight for {deduplicationKey}"
              , DeduplicationKey = deduplicationKey
              , Priority         = priority
              , Category         = InsightCategory.General
        };

    private static Mock<IInsightProvider> MakeProvider( InsightCategory  category
                                                      , params Insight[] insights )
    {
        var mock = new Mock<IInsightProvider>();
        mock.SetupGet(provider => provider.Category).Returns(category);
        mock.Setup(provider => provider.GenerateAsync(It.IsAny<ConversationContext>()
                                                    , It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnumerable(insights));
        return mock;
    }

    private static async IAsyncEnumerable<Insight> ToAsyncEnumerable(IEnumerable<Insight> insights)
    {
        foreach (var insight in insights)
        {
            await Task.CompletedTask;
            yield return insight;
        }
    }

    private static async IAsyncEnumerable<Insight> FaultingAsync()
    {
#pragma warning disable CS0162 // unreachable code required to make this an async-iterator method
        await Task.CompletedTask;
        throw new InvalidOperationException("Provider exploded.");
        yield break;
#pragma warning restore CS0162
    }
}
