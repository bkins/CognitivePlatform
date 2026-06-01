using Moq;
using CognitivePlatform.Api.Conversation;
using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Domains.Journal;
using CognitivePlatform.Api.Domains.Journal.Interfaces;
using CognitivePlatform.Api.Insights;
using CognitivePlatform.Api.Insights.Models;
using CognitivePlatform.Api.Interpreter;
using CognitivePlatform.Api.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace CognitivePlatform.Tests;

public class CognitiveDistortionInsightProviderTests
{
    private readonly Mock<IJournalService>                    _journalMock     = new();
    private readonly Mock<ILlmRouter>                         _routerMock      = new();
    private readonly Mock<IObjectStore>                        _objectStoreMock = new();
    private readonly CognitiveDistortionInsightProvider       _provider;

    public CognitiveDistortionInsightProviderTests()
    {
        _provider = new CognitiveDistortionInsightProvider(
            _journalMock.Object
          , _routerMock.Object
          , _objectStoreMock.Object
          , NullLogger<CognitiveDistortionInsightProvider>.Instance);
    }

    private static ConversationContext MakeContext() => new("test-session");

    private static async Task<List<Insight>> CollectAsync(IAsyncEnumerable<Insight> source)
    {
        var result = new List<Insight>();
        await foreach (var insight in source)
            result.Add(insight);
        return result;
    }

    private static JournalEntryWithRevision MakeEntry(string text)
        => new(
            new JournalEntry { Id = Guid.NewGuid().ToString("N"), CreatedUtc = DateTimeOffset.UtcNow.AddDays(-1) }
          , new JournalRevision
            {
                RevisionId = Guid.NewGuid().ToString("N")
              , EntryId    = Guid.NewGuid().ToString("N")
              , Text       = text
            }
          , IsEdited: false);

    private void SetupFlagEnabled()
        => _objectStoreMock
               .Setup(store => store.Get<CognitiveDistortionSettings>(
                   CognitiveDistortionInsightProvider.FlagKey
                 , It.IsAny<string?>()))
               .Returns(new CognitiveDistortionSettings { IsEnabled = true });

    private void SetupFlagDisabledOrMissing()
        => _objectStoreMock
               .Setup(store => store.Get<CognitiveDistortionSettings>(
                   CognitiveDistortionInsightProvider.FlagKey
                 , It.IsAny<string?>()))
               .Returns((CognitiveDistortionSettings?)null);

    private void SetupLlmResponse(string json)
        => _routerMock
               .Setup(router => router.SendAsync(
                   It.IsAny<string>()
                 , It.IsAny<ConversationContext>()
                 , It.IsAny<TaskComplexity>()
                 , It.IsAny<CancellationToken>()))
               .ReturnsAsync(new LlmResponse { Content = json });

    // ====================================================================
    // Opt-in gate — suppresses when flag is not set
    // ====================================================================

    [Fact]
    public async Task GenerateAsync_YieldsNothing_WhenOptInFlagIsNotSet()
    {
        SetupFlagDisabledOrMissing();
        _journalMock
            .Setup(service => service.ListEntries(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>()))
            .Returns(Array.Empty<JournalEntryWithRevision>());

        var insights = await CollectAsync(_provider.GenerateAsync(MakeContext()));

        Assert.Empty(insights);
        _routerMock.Verify(router => router.SendAsync(
                It.IsAny<string>()
              , It.IsAny<ConversationContext>()
              , It.IsAny<TaskComplexity>()
              , It.IsAny<CancellationToken>())
            , Times.Never);
    }

    // ====================================================================
    // LLM detects distortion — insight fires
    // ====================================================================

    [Fact]
    public async Task GenerateAsync_YieldsInsight_WhenLlmDetectsCatastrophising()
    {
        SetupFlagEnabled();
        _journalMock
            .Setup(service => service.ListEntries(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>()))
            .Returns(new[] { MakeEntry("Everything is completely ruined. I always mess this up.") });

        SetupLlmResponse("""{"detected":true,"pattern":"all-or-nothing","message":"I noticed some pretty absolute language in your recent journals — 'always' and 'completely'. These patterns can feel more permanent than they are."}""");

        var insights = await CollectAsync(_provider.GenerateAsync(MakeContext()));

        var single = Assert.Single(insights);
        Assert.Equal("cog.distortion",          single.DeduplicationKey);
        Assert.Equal(InsightCategory.Wellbeing, single.Category);
        Assert.Equal(InsightPriority.Normal,    single.Priority);
        Assert.NotNull(single.Reasoning);
        Assert.Contains("all-or-nothing",       single.Reasoning.Explanation);
    }

    // ====================================================================
    // LLM finds no distortion — suppress
    // ====================================================================

    [Fact]
    public async Task GenerateAsync_YieldsNothing_WhenLlmFindsNoDistortion()
    {
        SetupFlagEnabled();
        _journalMock
            .Setup(service => service.ListEntries(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>()))
            .Returns(new[] { MakeEntry("Had a productive day. Finished the report.") });

        SetupLlmResponse("""{"detected":false,"pattern":"","message":""}""");

        var insights = await CollectAsync(_provider.GenerateAsync(MakeContext()));

        Assert.Empty(insights);
    }

    // ====================================================================
    // Deduplication key
    // ====================================================================

    [Fact]
    public async Task GenerateAsync_DeduplicationKey_IsCogDistortion()
    {
        SetupFlagEnabled();
        _journalMock
            .Setup(service => service.ListEntries(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>()))
            .Returns(new[] { MakeEntry("I never do anything right. It will never get better.") });

        SetupLlmResponse("""{"detected":true,"pattern":"catastrophising","message":"I noticed some fortune-telling language in your journal."}""");

        var insights = await CollectAsync(_provider.GenerateAsync(MakeContext()));

        var insight = Assert.Single(insights);
        Assert.Equal("cog.distortion", insight.DeduplicationKey);
    }

    // ====================================================================
    // Category property
    // ====================================================================

    [Fact]
    public void Category_IsWellbeing()
    {
        Assert.Equal(InsightCategory.Wellbeing, _provider.Category);
    }
}
