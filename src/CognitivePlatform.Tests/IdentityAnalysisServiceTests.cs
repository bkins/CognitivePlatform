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

        _identityServiceMock.Setup(svc => svc.GetSnapshotsAsync(It.IsAny<CancellationToken>()))
                            .ReturnsAsync([]);

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

    // ================================================================
    // ReflectOnChangeAsync
    // ================================================================

    [Fact]
    public async Task ReflectOnChangeAsync_ReturnsGracefulMessage_WhenFewerThanTwoSnapshotsInWindow()
    {
        var snapshot = new PersonalitySnapshot
                       {
                               Id               = "s1"
                             , Timestamp        = DateTime.UtcNow.AddDays(-10)
                             , NarrativeSummary = "A short narrative."
                       };

        _identityServiceMock.Setup(svc => svc.GetSnapshotsAsync(It.IsAny<CancellationToken>()))
                            .ReturnsAsync(new List<PersonalitySnapshot> { snapshot });

        var result = await _service.ReflectOnChangeAsync(string.Empty, TimeSpan.FromDays(30), CancellationToken.None);

        Assert.Contains("Not enough snapshot history", result);
    }

    [Fact]
    public async Task ReflectOnChangeAsync_ReturnsGracefulMessage_WhenNoSnapshotsExist()
    {
        _identityServiceMock.Setup(svc => svc.GetSnapshotsAsync(It.IsAny<CancellationToken>()))
                            .ReturnsAsync([]);

        var result = await _service.ReflectOnChangeAsync(string.Empty, TimeSpan.FromDays(182), CancellationToken.None);

        Assert.Contains("Not enough snapshot history", result);
    }

    [Fact]
    public async Task ReflectOnChangeAsync_CallsLlm_WhenTwoOrMoreSnapshotsExistInWindow()
    {
        var snapshots = new List<PersonalitySnapshot>
        {
            new() { Id = "s1", Timestamp = DateTime.UtcNow.AddDays(-60), NarrativeSummary = "Earlier narrative." }
          , new() { Id = "s2", Timestamp = DateTime.UtcNow.AddDays(-5),  NarrativeSummary = "Later narrative."   }
        };

        _identityServiceMock.Setup(svc => svc.GetSnapshotsAsync(It.IsAny<CancellationToken>()))
                            .ReturnsAsync(snapshots);

        _llmClientMock.Setup(client => client.SendAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                      .ReturnsAsync(new LlmResponse { Content = "You have grown significantly over this period." });

        var result = await _service.ReflectOnChangeAsync(string.Empty, TimeSpan.FromDays(90), CancellationToken.None);

        Assert.Equal("You have grown significantly over this period.", result);
        _llmClientMock.Verify(client => client.SendAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReflectOnChangeAsync_OnlyConsidersSnapshotsWithinWindow()
    {
        var snapshots = new List<PersonalitySnapshot>
        {
            new() { Id = "s-old",    Timestamp = DateTime.UtcNow.AddDays(-200), NarrativeSummary = "Very old snapshot." }
          , new() { Id = "s-recent", Timestamp = DateTime.UtcNow.AddDays(-10),  NarrativeSummary = "Recent snapshot."   }
        };

        _identityServiceMock.Setup(svc => svc.GetSnapshotsAsync(It.IsAny<CancellationToken>()))
                            .ReturnsAsync(snapshots);

        var result = await _service.ReflectOnChangeAsync(string.Empty, TimeSpan.FromDays(90), CancellationToken.None);

        Assert.Contains("Not enough snapshot history", result);
    }

    // ================================================================
    // IdentifyPatternsAsync
    // ================================================================

    [Fact]
    public async Task IdentifyPatternsAsync_CallsLlmAndReturnsNarrative()
    {
        _llmClientMock.Setup(client => client.SendAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                      .ReturnsAsync(new LlmResponse { Content = "- **Deep work pattern**: You tend to focus in long uninterrupted sessions." });

        var result = await _service.IdentifyPatternsAsync(string.Empty, CancellationToken.None);

        Assert.Contains("Deep work pattern", result);
    }

    [Fact]
    public async Task IdentifyPatternsAsync_ReadsLast90DaysOfJournalEntries()
    {
        _llmClientMock.Setup(client => client.SendAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                      .ReturnsAsync(new LlmResponse { Content = "patterns narrative" });

        await _service.IdentifyPatternsAsync(string.Empty, CancellationToken.None);

        _journalServiceMock.Verify(
            svc => svc.ListEntries(
                It.Is<DateTimeOffset?>(offset => offset.HasValue
                                              && offset.Value >= DateTimeOffset.UtcNow.AddDays(-91)
                                              && offset.Value <= DateTimeOffset.UtcNow.AddDays(-89))
              , null)
          , Times.Once);
    }

    [Fact]
    public async Task IdentifyPatternsAsync_OnlyIncludesConfirmedAssertionsInPrompt()
    {
        var assertions = new List<IdentityAssertion>
        {
            new() { Id = "a1", Topic = "work", Statement = "prefers async comms", UserConfirmed = true  }
          , new() { Id = "a2", Topic = "stress", Statement = "catastrophizes deadlines", UserConfirmed = false }
        };

        _identityServiceMock.Setup(svc => svc.GetAssertionsAsync(It.IsAny<CancellationToken>()))
                            .ReturnsAsync(assertions);

        string? capturedPrompt = null;
        _llmClientMock.Setup(client => client.SendAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                      .Callback<string, string?, CancellationToken>((prompt, _, _) => capturedPrompt = prompt)
                      .ReturnsAsync(new LlmResponse { Content = "patterns" });

        await _service.IdentifyPatternsAsync(string.Empty, CancellationToken.None);

        Assert.NotNull(capturedPrompt);
        Assert.Contains("prefers async comms",      capturedPrompt);
        Assert.DoesNotContain("catastrophizes deadlines", capturedPrompt);
    }

    // ================================================================
    // AnalyzeStressorsAsync
    // ================================================================

    [Fact]
    public async Task AnalyzeStressorsAsync_CallsLlmAndReturnsNarrative()
    {
        _llmClientMock.Setup(client => client.SendAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                      .ReturnsAsync(new LlmResponse { Content = "Context-switching is your primary stressor." });

        var result = await _service.AnalyzeStressorsAsync(string.Empty, CancellationToken.None);

        Assert.Equal("Context-switching is your primary stressor.", result);
    }

    [Fact]
    public async Task AnalyzeStressorsAsync_IncludesProfileStressorsInPrompt()
    {
        var profile = new PersonProfile { Stressors = ["context-switching", "unclear requirements"] };

        _identityServiceMock.Setup(svc => svc.GetProfileAsync(It.IsAny<CancellationToken>()))
                            .ReturnsAsync(profile);

        _identityServiceMock.Setup(svc => svc.GetSnapshotsAsync(It.IsAny<CancellationToken>()))
                            .ReturnsAsync([]);

        string? capturedPrompt = null;
        _llmClientMock.Setup(client => client.SendAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                      .Callback<string, string?, CancellationToken>((prompt, _, _) => capturedPrompt = prompt)
                      .ReturnsAsync(new LlmResponse { Content = "stressor analysis" });

        await _service.AnalyzeStressorsAsync(string.Empty, CancellationToken.None);

        Assert.NotNull(capturedPrompt);
        Assert.Contains("context-switching",      capturedPrompt);
        Assert.Contains("unclear requirements",   capturedPrompt);
    }

    // ================================================================
    // SummarizeGrowthAsync
    // ================================================================

    [Fact]
    public async Task SummarizeGrowthAsync_ReturnsGracefulMessage_WhenNoSnapshotsExist()
    {
        _identityServiceMock.Setup(svc => svc.GetSnapshotsAsync(It.IsAny<CancellationToken>()))
                            .ReturnsAsync([]);

        var result = await _service.SummarizeGrowthAsync(string.Empty, CancellationToken.None);

        Assert.Contains("No snapshot history yet", result);
    }

    [Fact]
    public async Task SummarizeGrowthAsync_CallsLlm_WhenSnapshotsExist()
    {
        var snapshots = new List<PersonalitySnapshot>
        {
            new() { Id = "s1", Timestamp = DateTime.UtcNow.AddDays(-60), ObservedStrengths = ["focus", "empathy"] }
          , new() { Id = "s2", Timestamp = DateTime.UtcNow.AddDays(-10), ObservedStrengths = ["focus", "empathy", "strategic thinking"] }
        };

        _identityServiceMock.Setup(svc => svc.GetSnapshotsAsync(It.IsAny<CancellationToken>()))
                            .ReturnsAsync(snapshots);

        _llmClientMock.Setup(client => client.SendAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                      .ReturnsAsync(new LlmResponse { Content = "You have developed stronger strategic thinking." });

        var result = await _service.SummarizeGrowthAsync(string.Empty, CancellationToken.None);

        Assert.Equal("You have developed stronger strategic thinking.", result);
    }

    [Fact]
    public async Task SummarizeGrowthAsync_UsesAtMostThreeRecentSnapshots()
    {
        var snapshots = Enumerable.Range(1, 6)
                                  .Select(index => new PersonalitySnapshot
                                                   {
                                                           Id               = $"s{index}"
                                                         , Timestamp        = DateTime.UtcNow.AddDays(-index * 20)
                                                         , ObservedStrengths = [$"strength-{index}"]
                                                   })
                                  .ToList();

        _identityServiceMock.Setup(svc => svc.GetSnapshotsAsync(It.IsAny<CancellationToken>()))
                            .ReturnsAsync(snapshots);

        string? capturedPrompt = null;
        _llmClientMock.Setup(client => client.SendAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                      .Callback<string, string?, CancellationToken>((prompt, _, _) => capturedPrompt = prompt)
                      .ReturnsAsync(new LlmResponse { Content = "growth narrative" });

        await _service.SummarizeGrowthAsync(string.Empty, CancellationToken.None);

        Assert.NotNull(capturedPrompt);
        Assert.Contains("strength-1", capturedPrompt);
        Assert.Contains("strength-2", capturedPrompt);
        Assert.Contains("strength-3", capturedPrompt);
        Assert.DoesNotContain("strength-4", capturedPrompt);
    }

    // ================================================================
    // CompareSnapshotsAsync
    // ================================================================

    [Fact]
    public async Task CompareSnapshotsAsync_ReturnsGracefulMessage_WhenFewerThanTwoSnapshotsExist()
    {
        _identityServiceMock.Setup(svc => svc.GetSnapshotsAsync(It.IsAny<CancellationToken>()))
                            .ReturnsAsync([new PersonalitySnapshot { Id = "s1" }]);

        var result = await _service.CompareSnapshotsAsync(
            string.Empty
          , new DateTime(2026, 1, 1)
          , new DateTime(2026, 5, 1)
          , CancellationToken.None);

        Assert.Contains("Not enough snapshot history", result);
    }

    [Fact]
    public async Task CompareSnapshotsAsync_ReturnsGracefulMessage_WhenBothDatesMapToSameSnapshot()
    {
        var snapshots = new List<PersonalitySnapshot>
        {
            new() { Id = "s1", Timestamp = new DateTime(2026, 3, 15) }
          , new() { Id = "s2", Timestamp = new DateTime(2026, 1,  1) }
        };

        _identityServiceMock.Setup(svc => svc.GetSnapshotsAsync(It.IsAny<CancellationToken>()))
                            .ReturnsAsync(snapshots);

        var result = await _service.CompareSnapshotsAsync(
            string.Empty
          , new DateTime(2026, 3, 14)
          , new DateTime(2026, 3, 16)
          , CancellationToken.None);

        Assert.Contains("same snapshot", result);
    }

    [Fact]
    public async Task CompareSnapshotsAsync_CallsLlm_WhenTwoDistinctSnapshotsFound()
    {
        var snapshots = new List<PersonalitySnapshot>
        {
            new() { Id = "s-jan", Timestamp = new DateTime(2026, 1, 10), NarrativeSummary = "January snapshot." }
          , new() { Id = "s-may", Timestamp = new DateTime(2026, 5, 10), NarrativeSummary = "May snapshot."     }
        };

        _identityServiceMock.Setup(svc => svc.GetSnapshotsAsync(It.IsAny<CancellationToken>()))
                            .ReturnsAsync(snapshots);

        _llmClientMock.Setup(client => client.SendAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                      .ReturnsAsync(new LlmResponse { Content = "You shifted considerably from January to May." });

        var result = await _service.CompareSnapshotsAsync(
            string.Empty
          , new DateTime(2026, 1, 1)
          , new DateTime(2026, 5, 1)
          , CancellationToken.None);

        Assert.Equal("You shifted considerably from January to May.", result);
        _llmClientMock.Verify(client => client.SendAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
