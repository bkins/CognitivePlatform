using Moq;
using CognitivePlatform.Api.Domains.Personality;
using CognitivePlatform.Api.Domains.PersonaEngine;
using CognitivePlatform.Api.Domains.PersonaEngine.Models;

namespace CognitivePlatform.Tests;

public class HybridPersonaEngineTests
{
    private readonly Mock<IIntentAnalyzer>    _ruleAnalyzerMock    = new();
    private readonly Mock<IIntentAnalyzer>    _keywordAnalyzerMock = new();
    private readonly Mock<IIntentAnalyzer>    _llmAnalyzerMock     = new();
    private readonly Mock<IPersonalityService> _serviceMock         = new();
    private readonly HybridPersonaEngine       _engine;

    public HybridPersonaEngineTests()
    {
        _engine = new HybridPersonaEngine(_ruleAnalyzerMock.Object
                                        , _keywordAnalyzerMock.Object
                                        , _llmAnalyzerMock.Object
                                        , _serviceMock.Object);

        ArrangeNoPersonalities();
    }

    // ================================================================
    // Weighted scoring — LLM arm (weight 0.5) dominates
    // ================================================================

    [Fact]
    public async Task ResolveAsync_UsesLlmResult_WhenLlmAloneExceedsThreshold()
    {
        ArrangeAnalyzers(rule:    (Intent.Unknown,       0.0,  null)
                        , keyword: (Intent.Unknown,       0.0,  null)
                        , llm:     (Intent.TechnicalHelp, 0.9,  null));

        var result = await _engine.ResolveAsync("some message");

        Assert.Equal(Intent.TechnicalHelp, result.Intent);
    }

    // ================================================================
    // Weighted scoring — unanimous agreement across all three arms
    // ================================================================

    [Fact]
    public async Task ResolveAsync_ReturnsIntent_WhenAllArmsAgree()
    {
        ArrangeAnalyzers(rule:    (Intent.Leadership, 0.85, null)
                        , keyword: (Intent.Leadership, 0.8,  null)
                        , llm:     (Intent.Leadership, 0.9,  null));

        var result = await _engine.ResolveAsync("team conflict meeting");

        Assert.Equal(Intent.Leadership, result.Intent);
    }

    // ================================================================
    // Weighted scoring — keyword + rule combine to beat absent LLM signal
    // ================================================================

    [Fact]
    public async Task ResolveAsync_ReturnsIntentFromWeightedCombination_WhenArmsDisagree()
    {
        // keyword (0.3 weight × 0.8 conf = 0.24) + rule (0.2 weight × 0.9 conf = 0.18) = 0.42 for Motivation
        // llm contributes 0.0 for Motivation, so combined score is 0.42 which exceeds the 0.4 threshold
        ArrangeAnalyzers(rule:    (Intent.Motivation, 0.9, null)
                        , keyword: (Intent.Motivation, 0.8, null)
                        , llm:     (Intent.Unknown,    0.0, null));

        var result = await _engine.ResolveAsync("please motivate me");

        Assert.Equal(Intent.Motivation, result.Intent);
    }

    // ================================================================
    // Fallback — all analyzers return Unknown → result is Unknown
    // ================================================================

    [Fact]
    public async Task ResolveAsync_ReturnsUnknown_WhenAllAnalyzersReturnUnknown()
    {
        ArrangeAnalyzers(rule:    (Intent.Unknown, 0.1, null)
                        , keyword: (Intent.Unknown, 0.0, null)
                        , llm:     (Intent.Unknown, 0.0, null));

        var result = await _engine.ResolveAsync("some random message");

        Assert.Equal(Intent.Unknown, result.Intent);
        Assert.Null(result.Personality);
    }

    // ================================================================
    // Null / empty input — Unknown without calling analyzers
    // ================================================================

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null!)]
    public async Task ResolveAsync_ReturnsUnknown_WhenMessageIsNullOrWhiteSpace(string? message)
    {
        var result = await _engine.ResolveAsync(message!);

        Assert.Equal(Intent.Unknown, result.Intent);
        Assert.Null(result.Personality);

        _ruleAnalyzerMock.Verify(analyzer => analyzer.AnalyzeAsync(It.IsAny<string>()
                                                                  , It.IsAny<CancellationToken>())
                               , Times.Never);
    }

    // ================================================================
    // Persona name priority — LLM name wins over keyword and rule
    // ================================================================

    [Fact]
    public async Task ResolveAsync_UsesSuggestedNameFromLlm_WhenLlmMatchesWinner()
    {
        var technicalPersona = new PersonalityDefinition
                               {
                                   Id   = "tech-id"
                                 , Name = "TechnicalHelp"
                               };

        _serviceMock.Setup(service => service.GetAllAsync())
                    .ReturnsAsync(new List<PersonalityDefinition> { technicalPersona });

        ArrangeAnalyzers(rule:    (Intent.TechnicalHelp, 0.9, "TechnicalHelper")
                        , keyword: (Intent.TechnicalHelp, 0.8, "TechnicalHelper")
                        , llm:     (Intent.TechnicalHelp, 0.9, "TechnicalHelp"));

        var result = await _engine.ResolveAsync("fix my bug");

        Assert.NotNull(result.Personality);
        Assert.Equal("TechnicalHelp", result.Personality!.Name);
    }

    // ================================================================
    // Metadata — all three arm results recorded in IntentAnalysisResult
    // ================================================================

    [Fact]
    public async Task ResolveAsync_PopulatesMetadata_WithAllThreeArmResults()
    {
        ArrangeAnalyzers(rule:    (Intent.Leadership,    0.85, null)
                        , keyword: (Intent.Leadership,    0.8,  null)
                        , llm:     (Intent.TechnicalHelp, 0.9,  null));

        var result = await _engine.ResolveAsync("team meeting and code review");

        Assert.True(result.IntentAnalysisResult.Metadata.ContainsKey("RuleIntent"));
        Assert.True(result.IntentAnalysisResult.Metadata.ContainsKey("KeywordIntent"));
        Assert.True(result.IntentAnalysisResult.Metadata.ContainsKey("LlmIntent"));
    }

    // ================================================================
    // Personality resolution — resolved personality is attached
    // ================================================================

    [Fact]
    public async Task ResolveAsync_AttachesResolvedPersonality_WhenPersonalityExists()
    {
        var persona = new PersonalityDefinition { Id = "p1", Name = "Motivator" };

        _serviceMock.Setup(service => service.GetAllAsync())
                    .ReturnsAsync(new List<PersonalityDefinition> { persona });

        ArrangeAnalyzers(rule:    (Intent.Unknown,   0.0, null)
                        , keyword: (Intent.Motivation, 0.8, "Motivator")
                        , llm:     (Intent.Motivation, 0.9, "Motivator"));

        var result = await _engine.ResolveAsync("please inspire me");

        Assert.NotNull(result.Personality);
        Assert.Equal("Motivator", result.Personality!.Name);
    }

    // --- Helpers -----------------------------------------------------------------

    private void ArrangeAnalyzers(
        (Intent intent, double confidence, string? personaName)  rule
      , (Intent intent, double confidence, string? personaName)  keyword
      , (Intent intent, double confidence, string? personaName)  llm)
    {
        _ruleAnalyzerMock
            .Setup(analyzer => analyzer.AnalyzeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IntentAnalysisResult
                          {
                              Intent               = rule.intent
                            , Confidence           = rule.confidence
                            , SuggestedPersonaName = rule.personaName
                          });

        _keywordAnalyzerMock
            .Setup(analyzer => analyzer.AnalyzeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IntentAnalysisResult
                          {
                              Intent               = keyword.intent
                            , Confidence           = keyword.confidence
                            , SuggestedPersonaName = keyword.personaName
                          });

        _llmAnalyzerMock
            .Setup(analyzer => analyzer.AnalyzeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IntentAnalysisResult
                          {
                              Intent               = llm.intent
                            , Confidence           = llm.confidence
                            , SuggestedPersonaName = llm.personaName
                          });
    }

    private void ArrangeNoPersonalities()
    {
        _serviceMock.Setup(service => service.GetAllAsync())
                    .ReturnsAsync(new List<PersonalityDefinition>());

        _serviceMock.Setup(service => service.GetActiveAsync())
                    .ReturnsAsync((PersonalityDefinition?)null);
    }
}
