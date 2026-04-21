using Moq;
using CognitivePlatform.Api.Conversation;
using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Insights;
using CognitivePlatform.Api.Insights.Models;
using CognitivePlatform.Api.Models;
using CognitivePlatform.Api.Registry;
using Microsoft.Extensions.Logging.Abstractions;

namespace CognitivePlatform.Tests;

public class InsightEngineTests
{
    private readonly Mock<IActionRegistry>      _registryMock      = new();
    private readonly Mock<IInsightHistoryStore> _historyStoreMock   = new();
    private readonly Mock<IObjectStore>         _objectStoreMock    = new();
    private readonly InsightPolicy              _policy             = new();

    public InsightEngineTests()
    {
        // History store returns false by default (no dedup)
        _historyStoreMock
            .Setup(store => store.WasRecentlyEmittedAsync(It.IsAny<string>()
                                                         , It.IsAny<TimeSpan>()
                                                         , It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
    }

    private InsightEngine BuildEngine(params IInsightProvider[] providers)
        => new InsightEngine( providers
                            , _registryMock.Object
                            , _historyStoreMock.Object
                            , _objectStoreMock.Object
                            , _policy
                            , NullLogger<InsightEngine>.Instance );

    private static ConversationContext MakeContext(string? lastMessage = null)
    {
        var ctx = new ConversationContext("test-session");
        ctx.LastUserMessage = lastMessage;
        return ctx;
    }

    // ================================================================
    // FAULT ISOLATION
    // ================================================================

    [Fact]
    public async Task GenerateInsightsAsync_SkipsFaultingProvider_OtherProvidersStillRun()
    {
        var faultingMock = new Mock<IInsightProvider>();
        faultingMock.SetupGet(provider => provider.Category).Returns(InsightCategory.General);
        faultingMock.Setup(provider => provider.GenerateAsync( It.IsAny<ConversationContext>()
                                                              , It.IsAny<IObjectStore>()
                                                              , It.IsAny<CancellationToken>()))
                    .Returns(FaultingAsync());

        var healthyInsight = MakeInsight("healthy.signal");
        var healthyMock    = MakeProvider(InsightCategory.Tasks, healthyInsight);

        _registryMock.Setup(reg => reg.FindByName(It.IsAny<string>())).Returns((ActionMetadata?)null);

        var engine = BuildEngine(faultingMock.Object, healthyMock.Object);
        var result = await engine.GenerateInsightsAsync(MakeContext());

        Assert.Single(result);
        Assert.Equal("healthy.signal", result[0].DeduplicationKey);
    }

    // ================================================================
    // DEDUPLICATION
    // ================================================================

    [Fact]
    public async Task GenerateInsightsAsync_EmitsOnce_WhenSameDeduplicationKeyFromTwoProviders()
    {
        const string sharedKey = "shared.dedup.key";
        var insight1 = MakeInsight(sharedKey, priority: InsightPriority.Normal);
        var insight2 = MakeInsight(sharedKey, priority: InsightPriority.High);

        var provider1 = MakeProvider(InsightCategory.General, insight1);
        var provider2 = MakeProvider(InsightCategory.Tasks,   insight2);

        _registryMock.Setup(reg => reg.FindByName(It.IsAny<string>())).Returns((ActionMetadata?)null);

        var engine = BuildEngine(provider1.Object, provider2.Object);
        var result = await engine.GenerateInsightsAsync(MakeContext());

        Assert.Single(result);
        Assert.Equal(sharedKey, result[0].DeduplicationKey);
    }

    // ================================================================
    // CAP AT MaxPerTurn
    // ================================================================

    [Fact]
    public async Task GenerateInsightsAsync_CapsResults_AtMaxPerTurn_HighestPriorityWins()
    {
        // Default policy has MaxPerTurn = 2; generate 3 insights
        var low    = MakeInsight("low.key",    priority: InsightPriority.Low);
        var normal = MakeInsight("normal.key", priority: InsightPriority.Normal);
        var high   = MakeInsight("high.key",   priority: InsightPriority.High);

        var provider = MakeProvider(InsightCategory.General, low, normal, high);

        _registryMock.Setup(reg => reg.FindByName(It.IsAny<string>())).Returns((ActionMetadata?)null);

        var engine = BuildEngine(provider.Object);
        var result = await engine.GenerateInsightsAsync(MakeContext());

        Assert.Equal(2, result.Count);
        Assert.Contains(result, insight => insight.DeduplicationKey == "high.key");
        Assert.Contains(result, insight => insight.DeduplicationKey == "normal.key");
        Assert.DoesNotContain(result, insight => insight.DeduplicationKey == "low.key");
    }

    // ================================================================
    // ACTION VALIDATION
    // ================================================================

    [Fact]
    public async Task GenerateInsightsAsync_SuppressesInsight_WhenSuggestedActionNotInRegistry()
    {
        var invalidInsight = new Insight
                             {
                                     Message          = "Do something."
                                   , SuggestedAction  = "NonExistentAction"
                                   , DeduplicationKey = "invalid.action"
                                   , Priority         = InsightPriority.High
                             };
        var validInsight = MakeInsight("valid.no.action");

        var provider = MakeProvider(InsightCategory.General, invalidInsight, validInsight);

        // Registry returns null for any name (action not found)
        _registryMock.Setup(reg => reg.FindByName("NonExistentAction")).Returns((ActionMetadata?)null);
        _registryMock.Setup(reg => reg.FindByName(It.IsAny<string>())).Returns((ActionMetadata?)null);

        var engine = BuildEngine(provider.Object);
        var result = await engine.GenerateInsightsAsync(MakeContext());

        Assert.Single(result);
        Assert.Equal("valid.no.action", result[0].DeduplicationKey);
    }

    // ================================================================
    // ConversationReflectionInsightProvider
    // ================================================================

    [Fact]
    public async Task ConversationReflectionInsightProvider_GeneratesInsight_WhenStressLanguageDetected()
    {
        var provider = new ConversationReflectionInsightProvider();
        var context  = MakeContext("I'm completely overwhelmed with everything on my plate.");

        var insights = new List<Insight>();
        await foreach (var insight in provider.GenerateAsync(context, _objectStoreMock.Object))
            insights.Add(insight);

        Assert.Single(insights);
        Assert.Equal(InsightCategory.Reflection, insights[0].Category);
        Assert.Equal("AddJournalEntry",          insights[0].SuggestedAction);
        Assert.Contains("reflection.stress-detected.", insights[0].DeduplicationKey);
    }

    [Fact]
    public async Task ConversationReflectionInsightProvider_GeneratesNoInsight_WhenNeutralLanguage()
    {
        var provider = new ConversationReflectionInsightProvider();
        var context  = MakeContext("What are my tasks for today?");

        var insights = new List<Insight>();
        await foreach (var insight in provider.GenerateAsync(context, _objectStoreMock.Object))
            insights.Add(insight);

        Assert.Empty(insights);
    }

    [Fact]
    public async Task ConversationReflectionInsightProvider_GeneratesNoInsight_WhenMessageIsNull()
    {
        var provider = new ConversationReflectionInsightProvider();
        var context  = MakeContext(null);

        var insights = new List<Insight>();
        await foreach (var insight in provider.GenerateAsync(context, _objectStoreMock.Object))
            insights.Add(insight);

        Assert.Empty(insights);
    }

    // ================================================================
    // HELPERS
    // ================================================================

    private static Insight MakeInsight( string          deduplicationKey
                                      , InsightPriority priority = InsightPriority.Normal )
        => new Insight
           {
                   Message          = $"Insight for {deduplicationKey}"
                 , DeduplicationKey = deduplicationKey
                 , Priority         = priority
                 , Category         = InsightCategory.General
           };

    private static Mock<IInsightProvider> MakeProvider( InsightCategory category
                                                       , params Insight[] insights )
    {
        var mock = new Mock<IInsightProvider>();
        mock.SetupGet(provider => provider.Category).Returns(category);
        mock.Setup(provider => provider.GenerateAsync( It.IsAny<ConversationContext>()
                                                     , It.IsAny<IObjectStore>()
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
