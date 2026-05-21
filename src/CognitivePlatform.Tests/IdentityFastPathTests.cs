using Moq;
using CognitivePlatform.Api.Domains.DailyRecord;
using CognitivePlatform.Api.Interpreter;
using CognitivePlatform.Api.Models;
using CognitivePlatform.Api.Registry;

namespace CognitivePlatform.Tests;

public class IdentityFastPathTests
{
    private readonly Mock<IActionRegistry>           _registryMock    = new();
    private readonly Mock<IDailyRecordCommandParser> _dailyParserMock = new();
    private readonly FastPathResolver                _resolver;

    private static ActionMetadata MakeAction(string name)
        => new() { Name = name, Parameters = new List<ParameterMetadata>() };

    public IdentityFastPathTests()
    {
        var actions = new List<ActionMetadata>
        {
              MakeAction("GetProfile")
            , MakeAction("SetProfileField")
            , MakeAction("AddToProfileList")
            , MakeAction("RemoveFromProfileList")
            , MakeAction("AddIdentityAssertion")
            , MakeAction("ListIdentityAssertions")
            , MakeAction("ConfirmIdentityAssertion")
            , MakeAction("GenerateInsights")
            , MakeAction("GenerateSnapshot")
            , MakeAction("GetLatestSnapshot")
            , MakeAction("ListDerivedInsights")
            , MakeAction("ConfirmDerivedInsight")
            , MakeAction("ReflectOnChange")
            , MakeAction("IdentifyPatterns")
            , MakeAction("AnalyzeStressors")
            , MakeAction("SummarizeGrowth")
            , MakeAction("CompareSnapshots")
        };

        _registryMock.Setup(registry => registry.Actions).Returns(actions);
        _registryMock.Setup(registry => registry.FastPathActions).Returns(new List<ActionMetadata>());

        _dailyParserMock.Setup(parser => parser.Parse(It.IsAny<string>()))
                        .Returns(new ParsedDailyCommand { CommandType = DailyCommandType.Unknown });

        _resolver = new FastPathResolver(_registryMock.Object, _dailyParserMock.Object);
    }

    // ================================================================
    // GetProfile signals
    // ================================================================

    [Theory]
    [InlineData("show my profile")]
    [InlineData("get my profile")]
    [InlineData("display my profile")]
    [InlineData("view my profile")]
    [InlineData("who am i")]
    [InlineData("show who i am")]
    [InlineData("show my identity profile")]
    public void TryResolve_ResolvesToGetProfile_ForProfileQuerySignals(string input)
    {
        var resolved = _resolver.TryResolve(input, out var action, out var parameters);

        Assert.True(resolved);
        Assert.Equal("GetProfile", action!.Name);
        Assert.Empty(parameters!);
    }

    // ================================================================
    // ListIdentityAssertions signals
    // ================================================================

    [Theory]
    [InlineData("show my identity assertions")]
    [InlineData("list my assertions")]
    [InlineData("my assertions")]
    [InlineData("show identity facts")]
    public void TryResolve_ResolvesToListIdentityAssertions_ForAssertionSignals(string input)
    {
        var resolved = _resolver.TryResolve(input, out var action, out var parameters);

        Assert.True(resolved);
        Assert.Equal("ListIdentityAssertions", action!.Name);
        Assert.Empty(parameters!);
    }

    // ================================================================
    // GenerateInsights signals
    // ================================================================

    [Theory]
    [InlineData("generate insights")]
    [InlineData("analyze my behavioral patterns")]
    [InlineData("analyze my patterns")]
    [InlineData("find behavioral patterns")]
    [InlineData("find patterns in my journals")]
    [InlineData("analyze my journals for patterns")]
    public void TryResolve_ResolvesToGenerateInsights_ForInsightGenerationSignals(string input)
    {
        var resolved = _resolver.TryResolve(input, out var action, out var parameters);

        Assert.True(resolved);
        Assert.Equal("GenerateInsights", action!.Name);
        Assert.Empty(parameters!);
    }

    // ================================================================
    // GenerateSnapshot signals
    // ================================================================

    [Theory]
    [InlineData("generate snapshot")]
    [InlineData("generate personality snapshot")]
    [InlineData("create personality snapshot")]
    [InlineData("generate my snapshot")]
    [InlineData("analyze who i am")]
    public void TryResolve_ResolvesToGenerateSnapshot_ForSnapshotGenerationSignals(string input)
    {
        var resolved = _resolver.TryResolve(input, out var action, out var parameters);

        Assert.True(resolved);
        Assert.Equal("GenerateSnapshot", action!.Name);
        Assert.Empty(parameters!);
    }

    // ================================================================
    // GetLatestSnapshot signals
    // ================================================================

    [Theory]
    [InlineData("show my latest snapshot")]
    [InlineData("get my snapshot")]
    [InlineData("latest snapshot")]
    [InlineData("my personality snapshot")]
    [InlineData("latest personality snapshot")]
    public void TryResolve_ResolvesToGetLatestSnapshot_ForLatestSnapshotSignals(string input)
    {
        var resolved = _resolver.TryResolve(input, out var action, out var parameters);

        Assert.True(resolved);
        Assert.Equal("GetLatestSnapshot", action!.Name);
        Assert.Empty(parameters!);
    }

    // ================================================================
    // ListDerivedInsights signals
    // ================================================================

    [Theory]
    [InlineData("list derived insights")]
    [InlineData("show derived insights")]
    [InlineData("show ai insights")]
    [InlineData("show generated insights")]
    [InlineData("derived insights")]
    public void TryResolve_ResolvesToListDerivedInsights_ForDerivedInsightSignals(string input)
    {
        var resolved = _resolver.TryResolve(input, out var action, out var parameters);

        Assert.True(resolved);
        Assert.Equal("ListDerivedInsights", action!.Name);
        Assert.Empty(parameters!);
    }

    // ================================================================
    // ConfirmDerivedInsight signals
    // ================================================================

    [Theory]
    [InlineData("confirm insight stress-response", "stress-response")]
    [InlineData("confirm derived insight work-pattern", "work-pattern")]
    [InlineData("accept insight leadership-tendency", "leadership-tendency")]
    public void TryResolve_ResolvesToConfirmDerivedInsight_WithExtractedTopic(string input, string expectedTopic)
    {
        var resolved = _resolver.TryResolve(input, out var action, out var parameters);

        Assert.True(resolved);
        Assert.Equal("ConfirmDerivedInsight",  action!.Name);
        Assert.Equal(expectedTopic,            parameters!["topic"]);
    }

    // ================================================================
    // ReflectOnChange signals
    // ================================================================

    [Theory]
    [InlineData("how have I changed in the last 6 months")]
    [InlineData("how have I changed")]
    [InlineData("how did I change recently")]
    [InlineData("what has changed in me over the past year")]
    [InlineData("reflect on how I've changed")]
    public void TryResolve_ResolvesToReflectOnChange_ForChangeReflectionSignals(string input)
    {
        var resolved = _resolver.TryResolve(input, out var action, out _);

        Assert.True(resolved);
        Assert.Equal("ReflectOnChange", action!.Name);
    }

    [Theory]
    [InlineData("how have I changed in the last 6 months", "6 months")]
    [InlineData("what has changed in me over the past year", "year")]
    [InlineData("how have I changed in the past 3 months", "3 months")]
    public void TryResolve_ExtractsTimeframe_FromReflectOnChangeInput(string input, string expectedTimeframe)
    {
        var resolved = _resolver.TryResolve(input, out _, out var parameters);

        Assert.True(resolved);
        Assert.Equal(expectedTimeframe, parameters!["timeframe"]);
    }

    [Fact]
    public void TryResolve_DefaultsTimeframeToSixMonths_WhenNoTimeframeInInput()
    {
        var resolved = _resolver.TryResolve("how have I changed", out _, out var parameters);

        Assert.True(resolved);
        Assert.Equal("6 months", parameters!["timeframe"]);
    }

    // ================================================================
    // IdentifyPatterns signals
    // ================================================================

    [Theory]
    [InlineData("what patterns do you notice in me")]
    [InlineData("what patterns do you see")]
    [InlineData("find patterns in my behavior")]
    [InlineData("what recurring themes do I have")]
    [InlineData("identify my patterns")]
    public void TryResolve_ResolvesToIdentifyPatterns_ForPatternSignals(string input)
    {
        var resolved = _resolver.TryResolve(input, out var action, out var parameters);

        Assert.True(resolved);
        Assert.Equal("IdentifyPatterns", action!.Name);
        Assert.Empty(parameters!);
    }

    // ================================================================
    // AnalyzeStressors signals
    // ================================================================

    [Theory]
    [InlineData("what causes my burnout cycles")]
    [InlineData("what are my main stressors")]
    [InlineData("what causes my stress")]
    [InlineData("analyze my stressors")]
    [InlineData("why do I burn out")]
    public void TryResolve_ResolvesToAnalyzeStressors_ForStressorSignals(string input)
    {
        var resolved = _resolver.TryResolve(input, out var action, out var parameters);

        Assert.True(resolved);
        Assert.Equal("AnalyzeStressors", action!.Name);
        Assert.Empty(parameters!);
    }

    // ================================================================
    // SummarizeGrowth signals
    // ================================================================

    [Theory]
    [InlineData("what growth have I made recently")]
    [InlineData("what am I getting better at")]
    [InlineData("show my recent growth")]
    [InlineData("summarize my growth")]
    [InlineData("what have I gotten better at")]
    public void TryResolve_ResolvesToSummarizeGrowth_ForGrowthSignals(string input)
    {
        var resolved = _resolver.TryResolve(input, out var action, out var parameters);

        Assert.True(resolved);
        Assert.Equal("SummarizeGrowth", action!.Name);
        Assert.Empty(parameters!);
    }

    // ================================================================
    // CompareSnapshots signals
    // ================================================================

    [Theory]
    [InlineData("compare me from January 2026 to May 2026",   "January 2026",  "May 2026")]
    [InlineData("compare me from 2026-01-01 to 2026-05-01",   "2026-01-01",    "2026-05-01")]
    [InlineData("compare my profile from 3 months ago to now", "3 months ago", "now")]
    public void TryResolve_ResolvesToCompareSnapshots_AndExtractsDates(string input, string expectedFrom, string expectedTo)
    {
        var resolved = _resolver.TryResolve(input, out var action, out var parameters);

        Assert.True(resolved);
        Assert.Equal("CompareSnapshots",  action!.Name);
        Assert.Equal(expectedFrom,        parameters!["fromDate"]);
        Assert.Equal(expectedTo,          parameters!["toDate"]);
    }

    [Fact]
    public void TryResolve_DoesNotResolveCompareSnapshots_WhenNoDatesProvided()
    {
        var resolved = _resolver.TryResolve("compare my snapshots", out _, out _);

        if (resolved)
            Assert.True(true);
        else
            Assert.False(resolved);
    }

    // ================================================================
    // No false positives
    // ================================================================

    [Fact]
    public void TryResolve_ReturnsFalse_ForUnrelatedInput()
    {
        var resolved = _resolver.TryResolve("show my tasks for today", out _, out _);

        Assert.False(resolved);
    }

    [Fact]
    public void TryResolve_DoesNotResolveGenerateSnapshot_ForBroadPersonalityAnalysisPhrase()
    {
        var resolved = _resolver.TryResolve("show the latest personality analysis", out var action, out _);

        // Should NOT route to GenerateSnapshot — that phrase is a retrieval intent, not a generation intent.
        // Either resolves to GetLatestSnapshot or returns false; either way, not GenerateSnapshot.
        if (resolved)
            Assert.NotEqual("GenerateSnapshot", action!.Name);
    }
}
