using Moq;
using CognitivePlatform.Api.Domains.Identity;
using CognitivePlatform.Api.Domains.Journal.Interfaces;
using CognitivePlatform.Api.Domains.Tasks;
using CognitivePlatform.Api.Interpreter;
using CognitivePlatform.Api.Models;
using Microsoft.Extensions.Logging;

namespace CognitivePlatform.Tests;

public class IdentityAnalysisServiceTests
{
    private readonly Mock<IIdentityService>              _identityServiceMock = new();
    private readonly Mock<IJournalService>               _journalServiceMock  = new();
    private readonly Mock<ITaskService>                  _taskServiceMock     = new();
    private readonly Mock<ILlmClient>                    _llmClientMock       = new();
    private readonly Mock<ILogger<IdentityAnalysisService>> _loggerMock        = new();
    private readonly IdentityAnalysisService             _service;

    public IdentityAnalysisServiceTests()
    {
        _identityServiceMock.Setup(svc => svc.GetAssertionsAsync(It.IsAny<CancellationToken>()))
                            .ReturnsAsync([]);

        _identityServiceMock.Setup(svc => svc.GetProfileAsync(It.IsAny<CancellationToken>()))
                            .ReturnsAsync(new PersonProfile());

        _identityServiceMock.Setup(svc => svc.GetDerivedInsightsAsync(It.IsAny<CancellationToken>()))
                            .ReturnsAsync([]);

        _identityServiceMock.Setup(svc => svc.AddDerivedInsightAsync(It.IsAny<DerivedInsight>(), It.IsAny<CancellationToken>()))
                            .Returns(Task.CompletedTask);

        _identityServiceMock.Setup(svc => svc.AddSnapshotAsync(It.IsAny<PersonalitySnapshot>(), It.IsAny<CancellationToken>()))
                            .Returns(Task.CompletedTask);

        _journalServiceMock.Setup(svc => svc.ListEntries(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>()))
                           .Returns([]);

        _taskServiceMock.Setup(svc => svc.GetActive())
                        .Returns([]);

        _taskServiceMock.Setup(svc => svc.GetCompleted())
                        .Returns([]);

        _service = new IdentityAnalysisService(
            _identityServiceMock.Object
          , _journalServiceMock.Object
          , _taskServiceMock.Object
          , _llmClientMock.Object
          , _loggerMock.Object);
    }

    // ================================================================
    // GenerateInsightsAsync — happy path
    // ================================================================

    [Fact]
    public async Task GenerateInsightsAsync_ParsesValidJsonInsights()
    {
        var llmJson = """
{
  "insights": [
    {
      "insightType": "stress-response",
      "description": "tends to catastrophize timelines when under pressure",
      "confidence": 0.75,
      "sourceReferences": ["entry-abc"]
    },
    {
      "insightType": "work-pattern",
      "description": "most productive in early morning sessions",
      "confidence": 0.85,
      "sourceReferences": []
    }
  ]
}
""";

        _llmClientMock.Setup(client => client.SendAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                      .ReturnsAsync(new LlmResponse { Content = llmJson });

        var results = await _service.GenerateInsightsAsync(string.Empty, CancellationToken.None);

        Assert.Equal(2, results.Count);
        Assert.Equal("stress-response",                              results[0].InsightType);
        Assert.Equal("tends to catastrophize timelines when under pressure", results[0].Description);
        Assert.Equal(0.75,                                           results[0].Confidence);
        Assert.False(results[0].UserConfirmed);
        Assert.Equal("work-pattern", results[1].InsightType);
    }

    [Fact]
    public async Task GenerateInsightsAsync_SavesEachInsightViaService()
    {
        var llmJson = """
{
  "insights": [
    { "insightType": "leadership-tendency", "description": "prefers servant leadership style", "confidence": 0.7, "sourceReferences": [] }
  ]
}
""";

        _llmClientMock.Setup(client => client.SendAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                      .ReturnsAsync(new LlmResponse { Content = llmJson });

        await _service.GenerateInsightsAsync(string.Empty, CancellationToken.None);

        _identityServiceMock.Verify(
            svc => svc.AddDerivedInsightAsync(It.IsAny<DerivedInsight>(), It.IsAny<CancellationToken>())
          , Times.Once);
    }

    [Fact]
    public async Task GenerateInsightsAsync_ParsesMarkdownWrappedJson()
    {
        var markdownResponse = """
```json
{
  "insights": [
    { "insightType": "communication-style", "description": "prefers async written communication", "confidence": 0.65, "sourceReferences": [] }
  ]
}
```
""";

        _llmClientMock.Setup(client => client.SendAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                      .ReturnsAsync(new LlmResponse { Content = markdownResponse });

        var results = await _service.GenerateInsightsAsync(string.Empty, CancellationToken.None);

        Assert.Single(results);
        Assert.Equal("communication-style", results[0].InsightType);
    }

    [Fact]
    public async Task GenerateInsightsAsync_ClampConfidenceTo_ZeroToOne()
    {
        var llmJson = """
{
  "insights": [
    { "insightType": "test-type", "description": "some description", "confidence": 1.5, "sourceReferences": [] }
  ]
}
""";

        _llmClientMock.Setup(client => client.SendAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                      .ReturnsAsync(new LlmResponse { Content = llmJson });

        var results = await _service.GenerateInsightsAsync(string.Empty, CancellationToken.None);

        Assert.Single(results);
        Assert.Equal(1.0, results[0].Confidence);
    }

    // ================================================================
    // GenerateInsightsAsync — graceful failure
    // ================================================================

    [Fact]
    public async Task GenerateInsightsAsync_ThrowsAndLogs_WhenLlmFails()
    {
        _llmClientMock.Setup(client => client.SendAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                      .ThrowsAsync(new HttpRequestException("connection refused"));

        await Assert.ThrowsAsync<HttpRequestException>(
            () => _service.GenerateInsightsAsync(string.Empty, CancellationToken.None));

        _loggerMock.Verify(
            logger => logger.Log(
                LogLevel.Error
              , It.IsAny<EventId>()
              , It.IsAny<It.IsAnyType>()
              , It.IsAny<Exception?>()
              , It.IsAny<Func<It.IsAnyType, Exception?, string>>())
          , Times.Once);
    }

    [Fact]
    public async Task GenerateInsightsAsync_ReturnsEmpty_WhenLlmReturnsEmptyContent()
    {
        _llmClientMock.Setup(client => client.SendAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                      .ReturnsAsync(new LlmResponse { Content = string.Empty });

        var results = await _service.GenerateInsightsAsync(string.Empty, CancellationToken.None);

        Assert.Empty(results);
    }

    [Fact]
    public async Task GenerateInsightsAsync_ReturnsEmpty_WhenLlmReturnsMalformedJson()
    {
        _llmClientMock.Setup(client => client.SendAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                      .ReturnsAsync(new LlmResponse { Content = "this is not json" });

        var results = await _service.GenerateInsightsAsync(string.Empty, CancellationToken.None);

        Assert.Empty(results);
    }

    [Fact]
    public async Task GenerateInsightsAsync_SkipsInsights_WithMissingRequiredFields()
    {
        var llmJson = """
{
  "insights": [
    { "insightType": "", "description": "description without type", "confidence": 0.5, "sourceReferences": [] },
    { "insightType": "valid-type", "description": "", "confidence": 0.5, "sourceReferences": [] },
    { "insightType": "complete-type", "description": "complete description", "confidence": 0.6, "sourceReferences": [] }
  ]
}
""";

        _llmClientMock.Setup(client => client.SendAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                      .ReturnsAsync(new LlmResponse { Content = llmJson });

        var results = await _service.GenerateInsightsAsync(string.Empty, CancellationToken.None);

        Assert.Single(results);
        Assert.Equal("complete-type", results[0].InsightType);
    }

    // ================================================================
    // GenerateSnapshotAsync — happy path
    // ================================================================

    [Fact]
    public async Task GenerateSnapshotAsync_ParsesValidJsonSnapshot()
    {
        var llmJson = """
{
  "narrativeSummary": "Ben is a thoughtful software engineer who values deep work.",
  "dominantThemes": ["productivity", "deep work"],
  "activeStressors": ["deadline pressure"],
  "motivators": ["building products"],
  "observedStrengths": ["systems thinking"]
}
""";

        _llmClientMock.Setup(client => client.SendAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                      .ReturnsAsync(new LlmResponse { Content = llmJson });

        var snapshot = await _service.GenerateSnapshotAsync(string.Empty, CancellationToken.None);

        Assert.Equal("Ben is a thoughtful software engineer who values deep work.", snapshot.NarrativeSummary);
        Assert.Equal(new[] { "productivity", "deep work" },          snapshot.DominantThemes);
        Assert.Equal(new[] { "deadline pressure" },                  snapshot.ActiveStressors);
        Assert.Equal(new[] { "building products" },                  snapshot.Motivators);
        Assert.Equal(new[] { "systems thinking" },                   snapshot.ObservedStrengths);
    }

    [Fact]
    public async Task GenerateSnapshotAsync_SavesSnapshotViaService()
    {
        var llmJson = """
{
  "narrativeSummary": "Some narrative.",
  "dominantThemes": ["focus"],
  "activeStressors": [],
  "motivators": ["impact"],
  "observedStrengths": []
}
""";

        _llmClientMock.Setup(client => client.SendAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                      .ReturnsAsync(new LlmResponse { Content = llmJson });

        await _service.GenerateSnapshotAsync(string.Empty, CancellationToken.None);

        _identityServiceMock.Verify(
            svc => svc.AddSnapshotAsync(It.IsAny<PersonalitySnapshot>(), It.IsAny<CancellationToken>())
          , Times.Once);
    }

    [Fact]
    public async Task GenerateSnapshotAsync_ThrowsAndLogs_WhenLlmFails()
    {
        _llmClientMock.Setup(client => client.SendAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                      .ThrowsAsync(new InvalidOperationException("LLM unavailable"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.GenerateSnapshotAsync(string.Empty, CancellationToken.None));

        _loggerMock.Verify(
            logger => logger.Log(
                LogLevel.Error
              , It.IsAny<EventId>()
              , It.IsAny<It.IsAnyType>()
              , It.IsAny<Exception?>()
              , It.IsAny<Func<It.IsAnyType, Exception?, string>>())
          , Times.Once);
    }

    [Fact]
    public async Task GenerateSnapshotAsync_ReturnsEmptySnapshot_WhenResponseIsMalformed()
    {
        _llmClientMock.Setup(client => client.SendAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                      .ReturnsAsync(new LlmResponse { Content = "not json at all" });

        var snapshot = await _service.GenerateSnapshotAsync(string.Empty, CancellationToken.None);

        Assert.NotNull(snapshot);
        Assert.Empty(snapshot.NarrativeSummary);
    }

    [Fact]
    public async Task GenerateSnapshotAsync_DoesNotPersist_WhenSnapshotNarrativeIsEmpty()
    {
        _llmClientMock.Setup(client => client.SendAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                      .ReturnsAsync(new LlmResponse { Content = "not json at all" });

        await _service.GenerateSnapshotAsync(string.Empty, CancellationToken.None);

        _identityServiceMock.Verify(
            svc => svc.AddSnapshotAsync(It.IsAny<PersonalitySnapshot>(), It.IsAny<CancellationToken>())
          , Times.Never);
    }

    [Fact]
    public async Task GenerateSnapshotAsync_DoesNotPersist_WhenLlmReturnsEmptyNarrative()
    {
        var llmJson = """{ "narrativeSummary": "", "dominantThemes": [], "activeStressors": [], "motivators": [], "observedStrengths": [] }""";

        _llmClientMock.Setup(client => client.SendAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                      .ReturnsAsync(new LlmResponse { Content = llmJson });

        await _service.GenerateSnapshotAsync(string.Empty, CancellationToken.None);

        _identityServiceMock.Verify(
            svc => svc.AddSnapshotAsync(It.IsAny<PersonalitySnapshot>(), It.IsAny<CancellationToken>())
          , Times.Never);
    }

    // ================================================================
    // GenerateInsightsAsync — reads recent data
    // ================================================================

    [Fact]
    public async Task GenerateInsightsAsync_ReadsLast30DaysOfJournalEntries()
    {
        _llmClientMock.Setup(client => client.SendAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                      .ReturnsAsync(new LlmResponse { Content = """{ "insights": [] }""" });

        await _service.GenerateInsightsAsync(string.Empty, CancellationToken.None);

        _journalServiceMock.Verify(
            svc => svc.ListEntries(
                It.Is<DateTimeOffset?>(offset => offset.HasValue
                                              && offset.Value >= DateTimeOffset.UtcNow.AddDays(-31)
                                              && offset.Value <= DateTimeOffset.UtcNow.AddDays(-29))
              , null)
          , Times.Once);
    }

    [Fact]
    public async Task GenerateSnapshotAsync_ReadsLast14DaysOfJournalEntries()
    {
        _llmClientMock.Setup(client => client.SendAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                      .ReturnsAsync(new LlmResponse { Content = """{ "narrativeSummary": "", "dominantThemes": [], "activeStressors": [], "motivators": [], "observedStrengths": [] }""" });

        await _service.GenerateSnapshotAsync(string.Empty, CancellationToken.None);

        _journalServiceMock.Verify(
            svc => svc.ListEntries(
                It.Is<DateTimeOffset?>(offset => offset.HasValue
                                              && offset.Value >= DateTimeOffset.UtcNow.AddDays(-15)
                                              && offset.Value <= DateTimeOffset.UtcNow.AddDays(-13))
              , null)
          , Times.Once);
    }
}
