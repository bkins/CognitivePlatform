using Moq;
using CognitivePlatform.Api.Domains.DailyRecord;
using CognitivePlatform.Api.Interpreter;
using CognitivePlatform.Api.Models;
using CognitivePlatform.Api.Registry;

namespace CognitivePlatform.Tests;

public class SemanticSearchFastPathTests
{
    private readonly Mock<IActionRegistry>           _registryMock    = new();
    private readonly Mock<IDailyRecordCommandParser> _dailyParserMock = new();
    private readonly FastPathResolver                _resolver;

    private static ActionMetadata MakeAction(string name)
        => new() { Name = name, Parameters = new List<ParameterMetadata>() };

    public SemanticSearchFastPathTests()
    {
        var actions = new List<ActionMetadata>
        {
                MakeAction("SemanticSearch")
              , MakeAction("SemanticSearchInDomain")
              , MakeAction("FindSimilar")
              , MakeAction("ListActions")
        };

        _registryMock.Setup(registry => registry.Actions).Returns(actions);
        _registryMock.Setup(registry => registry.FastPathActions).Returns(new List<ActionMetadata>());
        _dailyParserMock.Setup(parser => parser.Parse(It.IsAny<string>()))
                        .Returns(new ParsedDailyCommand { CommandType = DailyCommandType.Unknown });

        _resolver = new FastPathResolver(_registryMock.Object, _dailyParserMock.Object);
    }

    // ================================================================
    // SemanticSearch — cross-domain phrases
    // ================================================================

    [Theory]
    [InlineData("What have I written about burnout?",        "burnout")]
    [InlineData("Find anything about project planning",      "project planning")]
    [InlineData("Search my notes for stress",                "stress")]
    [InlineData("What do I know about time management?",     "time management")]
    [InlineData("Find entries about work-life balance",      "work-life balance")]
    [InlineData("Find anything related to anxiety",          "anxiety")]
    public void TryResolve_ResolvesToSemanticSearch_ForCrossDomainPhrases(string input, string expectedQuery)
    {
        var resolved = _resolver.TryResolve(input, out var action, out var parameters);

        Assert.True(resolved);
        Assert.Equal("SemanticSearch", action!.Name);
        Assert.Equal(expectedQuery,    parameters!["query"]);
    }

    // ================================================================
    // SemanticSearchInDomain — journal-scoped phrases
    // ================================================================

    [Theory]
    [InlineData("Find journal entries about anxiety",    "journal", "anxiety")]
    [InlineData("Search my journal for gratitude",       "journal", "gratitude")]
    [InlineData("Look in my journal for stress",         "journal", "stress")]
    public void TryResolve_ResolvesToSemanticSearchInDomain_ForJournalPhrases(string input, string expectedDomain, string expectedQuery)
    {
        var resolved = _resolver.TryResolve(input, out var action, out var parameters);

        Assert.True(resolved);
        Assert.Equal("SemanticSearchInDomain", action!.Name);
        Assert.Equal(expectedDomain,           parameters!["domain"]);
        Assert.Equal(expectedQuery,            parameters!["query"]);
    }

    // ================================================================
    // SemanticSearchInDomain — task-scoped phrases
    // ================================================================

    [Theory]
    [InlineData("Search tasks for fitness",          "task", "fitness")]
    [InlineData("Search my tasks for report",        "task", "report")]
    [InlineData("Find tasks about documentation",    "task", "documentation")]
    public void TryResolve_ResolvesToSemanticSearchInDomain_ForTaskPhrases(string input, string expectedDomain, string expectedQuery)
    {
        var resolved = _resolver.TryResolve(input, out var action, out var parameters);

        Assert.True(resolved);
        Assert.Equal("SemanticSearchInDomain", action!.Name);
        Assert.Equal(expectedDomain,           parameters!["domain"]);
        Assert.Equal(expectedQuery,            parameters!["query"]);
    }

    // ================================================================
    // Non-matching — falls through
    // ================================================================

    [Theory]
    [InlineData("What have I written")]
    [InlineData("Find anything about")]
    [InlineData("Search my notes for")]
    public void TryResolve_ReturnsFalse_WhenQueryIsEmpty(string input)
    {
        var resolved = _resolver.TryResolve(input, out var action, out _);

        Assert.False(resolved || action?.Name == "SemanticSearch");
    }
}
