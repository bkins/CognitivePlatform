using Moq;
using CognitivePlatform.Api.Domains.Personality;
using CognitivePlatform.Api.Domains.PersonaEngine;

namespace CognitivePlatform.Tests;

public class RuleBasedPersonaEngineTests
{
    private readonly Mock<IPersonalityService> _serviceMock = new();
    private readonly RuleBasedPersonaEngine    _engine;

    public RuleBasedPersonaEngineTests()
    {
        _engine = new RuleBasedPersonaEngine(_serviceMock.Object);
    }

    // ================================================================
    // Intent classification — TechnicalHelp
    // ================================================================

    [Theory]
    [InlineData("I have a bug in my code")]
    [InlineData("Why won't this compile?")]
    [InlineData("There's an error in the stack trace")]
    public async Task ResolveAsync_ReturnsTechnicalHelpIntent_WhenMessageContainsTechnicalKeyword(string message)
    {
        ArrangeNoPersonalities();

        var result = await _engine.ResolveAsync(message);

        Assert.Equal(Intent.TechnicalHelp, result.Intent);
        Assert.Equal(0.9,                  result.IntentAnalysisResult.Confidence);
    }

    // ================================================================
    // Intent classification — Leadership
    // ================================================================

    [Theory]
    [InlineData("How do I manage my team better?")]
    [InlineData("There's a conflict between two colleagues")]
    [InlineData("I need help with team leadership")]
    public async Task ResolveAsync_ReturnsLeadershipIntent_WhenMessageContainsLeadershipKeyword(string message)
    {
        ArrangeNoPersonalities();

        var result = await _engine.ResolveAsync(message);

        Assert.Equal(Intent.Leadership, result.Intent);
        Assert.Equal(0.85,              result.IntentAnalysisResult.Confidence);
    }

    // ================================================================
    // Intent classification — Motivation
    // ================================================================

    [Theory]
    [InlineData("I'm feeling burnout")]
    [InlineData("Can you motivate me?")]
    [InlineData("I need something to inspire me today")]
    [InlineData("Please encourage me to keep going")]
    public async Task ResolveAsync_ReturnsMotivationIntent_WhenMessageContainsMotivationKeyword(string message)
    {
        ArrangeNoPersonalities();

        var result = await _engine.ResolveAsync(message);

        Assert.Equal(Intent.Motivation, result.Intent);
        Assert.Equal(0.8,               result.IntentAnalysisResult.Confidence);
    }

    // ================================================================
    // Intent classification — GeneralHelp
    // ================================================================

    [Theory]
    [InlineData("How do I get started?")]
    [InlineData("What is dependency injection?")]
    [InlineData("Can you explain SOLID principles?")]
    public async Task ResolveAsync_ReturnsGeneralHelpIntent_WhenMessageContainsGeneralKeyword(string message)
    {
        ArrangeNoPersonalities();

        var result = await _engine.ResolveAsync(message);

        Assert.Equal(Intent.GeneralHelp, result.Intent);
        Assert.Equal(0.7,                result.IntentAnalysisResult.Confidence);
    }

    // ================================================================
    // Intent classification — Unknown
    // ================================================================

    [Fact]
    public async Task ResolveAsync_ReturnsUnknownIntent_WhenMessageMatchesNoRule()
    {
        ArrangeNoPersonalities();

        var result = await _engine.ResolveAsync("Just saying hello there");

        Assert.Equal(Intent.Unknown, result.Intent);
        Assert.Equal(0.1,            result.IntentAnalysisResult.Confidence);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null!)]
    public async Task ResolveAsync_ReturnsUnknownIntent_WhenMessageIsNullOrWhiteSpace(string? message)
    {
        ArrangeNoPersonalities();

        var result = await _engine.ResolveAsync(message!);

        Assert.Equal(Intent.Unknown, result.Intent);
        Assert.Equal(0.0,            result.IntentAnalysisResult.Confidence);
    }

    // ================================================================
    // Personality resolution — matched by name
    // ================================================================

    [Fact]
    public async Task ResolveAsync_ReturnsMatchedPersonality_WhenNameNormalizedMatchesSuggestion()
    {
        var motivationalCoach = new PersonalityDefinition
                                {
                                        Id       = "coach-id"
                                      , Name     = "Motivational Coach"
                                      , IsActive = false
                                };

        _serviceMock.Setup(service => service.GetAllAsync())
                    .ReturnsAsync(new List<PersonalityDefinition> { motivationalCoach });

        var result = await _engine.ResolveAsync("I need something to inspire me");

        Assert.Equal(Intent.Motivation,  result.Intent);
        Assert.NotNull(result.Personality);
        Assert.Equal("Motivational Coach", result.Personality!.Name);
    }

    // ================================================================
    // Personality resolution — fallback to active
    // ================================================================

    [Fact]
    public async Task ResolveAsync_FallsBackToActivePersonality_WhenNoNameMatchFound()
    {
        var active = new PersonalityDefinition
                     {
                             Id       = "active-id"
                           , Name     = "Friendly Helper"
                           , IsActive = true
                     };

        _serviceMock.Setup(service => service.GetAllAsync())
                    .ReturnsAsync(new List<PersonalityDefinition> { active });

        _serviceMock.Setup(service => service.GetActiveAsync())
                    .ReturnsAsync(active);

        var result = await _engine.ResolveAsync("How do I manage my team better?");

        Assert.Equal(Intent.Leadership, result.Intent);
        Assert.NotNull(result.Personality);
        Assert.Equal("Friendly Helper", result.Personality!.Name);
    }

    // ================================================================
    // Personality resolution — fallback to first when no active
    // ================================================================

    [Fact]
    public async Task ResolveAsync_FallsBackToFirstPersonality_WhenNoActiveAndNoNameMatch()
    {
        var onlyPersonality = new PersonalityDefinition
                              {
                                      Id       = "first-id"
                                    , Name     = "Zen"
                                    , IsActive = false
                              };

        _serviceMock.Setup(service => service.GetAllAsync())
                    .ReturnsAsync(new List<PersonalityDefinition> { onlyPersonality });

        _serviceMock.Setup(service => service.GetActiveAsync())
                    .ReturnsAsync((PersonalityDefinition?)null);

        var result = await _engine.ResolveAsync("I have a bug in my code");

        Assert.Equal(Intent.TechnicalHelp, result.Intent);
        Assert.NotNull(result.Personality);
        Assert.Equal("Zen", result.Personality!.Name);
    }

    // ================================================================
    // Personality resolution — null when no personalities exist
    // ================================================================

    [Fact]
    public async Task ResolveAsync_ReturnsNullPersonality_WhenNoPersonalitiesExist()
    {
        _serviceMock.Setup(service => service.GetAllAsync())
                    .ReturnsAsync(new List<PersonalityDefinition>());

        _serviceMock.Setup(service => service.GetActiveAsync())
                    .ReturnsAsync((PersonalityDefinition?)null);

        var result = await _engine.ResolveAsync("I have a bug in my code");

        Assert.Equal(Intent.TechnicalHelp, result.Intent);
        Assert.Null(result.Personality);
    }

    // ================================================================
    // Result structure
    // ================================================================

    [Fact]
    public async Task ResolveAsync_PopulatesIntentAnalysisResultOnResult()
    {
        ArrangeNoPersonalities();

        var result = await _engine.ResolveAsync("There's a bug in the code");

        Assert.NotNull(result.IntentAnalysisResult);
        Assert.Equal(Intent.TechnicalHelp,  result.IntentAnalysisResult.Intent);
        Assert.Equal("TechnicalHelper",     result.IntentAnalysisResult.SuggestedPersonaName);
        Assert.Equal(result.Intent,         result.IntentAnalysisResult.Intent);
    }

    // --- Helpers -----------------------------------------------------------------

    private void ArrangeNoPersonalities()
    {
        _serviceMock.Setup(service => service.GetAllAsync())
                    .ReturnsAsync(new List<PersonalityDefinition>());

        _serviceMock.Setup(service => service.GetActiveAsync())
                    .ReturnsAsync((PersonalityDefinition?)null);
    }
}
