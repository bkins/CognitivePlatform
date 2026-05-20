using CognitivePlatform.Api.Domains.PersonaEngine;

namespace CognitivePlatform.Tests;

public class KeywordIntentAnalyzerTests
{
    private static readonly IReadOnlyDictionary<string, (Intent, string?)> DefaultRules =
        new Dictionary<string, (Intent, string?)>(StringComparer.OrdinalIgnoreCase)
        {
            ["code"]     = (Intent.TechnicalHelp, "TechnicalHelper")
          , ["bug"]      = (Intent.TechnicalHelp, "TechnicalHelper")
          , ["team"]     = (Intent.Leadership,    "LeadershipCoach")
          , ["motivate"] = (Intent.Motivation,    "Motivator")
        };

    private readonly KeywordIntentAnalyzer _analyzer = new(DefaultRules);

    // ================================================================
    // Match — returns 0.8 confidence with matched intent
    // ================================================================

    [Theory]
    [InlineData("I have a bug in my code")]
    [InlineData("This code won't compile")]
    public async Task AnalyzeAsync_ReturnsTechnicalHelp_WhenKeywordMatched(string message)
    {
        var result = await _analyzer.AnalyzeAsync(message);

        Assert.Equal(Intent.TechnicalHelp, result.Intent);
        Assert.Equal(0.8,                  result.Confidence);
        Assert.Equal("TechnicalHelper",    result.SuggestedPersonaName);
    }

    [Fact]
    public async Task AnalyzeAsync_ReturnsLeadership_WhenTeamKeywordPresent()
    {
        var result = await _analyzer.AnalyzeAsync("How do I manage my team?");

        Assert.Equal(Intent.Leadership,   result.Intent);
        Assert.Equal(0.8,                 result.Confidence);
        Assert.Equal("LeadershipCoach",   result.SuggestedPersonaName);
    }

    // ================================================================
    // No match — GeneralHelp fallback at 0.5
    // ================================================================

    [Fact]
    public async Task AnalyzeAsync_ReturnsGeneralHelp_WhenNoKeywordMatched()
    {
        var result = await _analyzer.AnalyzeAsync("Just saying hello there");

        Assert.Equal(Intent.GeneralHelp, result.Intent);
        Assert.Equal(0.5,                result.Confidence);
    }

    // ================================================================
    // Null / empty input — Unknown at 0.0
    // ================================================================

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null!)]
    public async Task AnalyzeAsync_ReturnsUnknown_WhenInputIsNullOrWhiteSpace(string? message)
    {
        var result = await _analyzer.AnalyzeAsync(message!);

        Assert.Equal(Intent.Unknown, result.Intent);
        Assert.Equal(0.0,            result.Confidence);
    }

    // ================================================================
    // Case-insensitive matching
    // ================================================================

    [Fact]
    public async Task AnalyzeAsync_MatchesKeyword_CaseInsensitively()
    {
        var result = await _analyzer.AnalyzeAsync("I am feeling BURNOUT... wait, no keyword for that");

        Assert.Equal(Intent.GeneralHelp, result.Intent);
    }

    [Fact]
    public async Task AnalyzeAsync_MatchesKeyword_WhenMixedCase()
    {
        var result = await _analyzer.AnalyzeAsync("There's a BUG in the system");

        Assert.Equal(Intent.TechnicalHelp, result.Intent);
    }
}
