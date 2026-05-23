using CognitivePlatform.Api.Interpreter;

namespace CognitivePlatform.Tests;

/// <summary>
/// ENH-19 Phase B: verifies the rule-based classifier gates.
/// Order of evaluation matters: Heavy keywords win over the Light gates,
/// otherwise short or prefix-matched messages fall to Light, and the
/// remaining traffic defaults to Standard.
/// </summary>
public class TaskComplexityClassifierTests
{
    private readonly TaskComplexityClassifier _classifier = new();

    // --------------------------------------------------------------
    // Heavy gate
    // --------------------------------------------------------------

    [Theory]
    [InlineData("please analyze my last week of journal entries")]
    [InlineData("Run an analysis on these tasks")]
    [InlineData("What pattern do you see across my mood entries?")]
    [InlineData("Take a moment to reflect on how the day went")]
    [InlineData("write a reflection about today's work")]
    [InlineData("synthesize what I've written into a single summary")]
    [InlineData("give me a comprehensive review of my month")]
    [InlineData("I want an in depth look at my habits")]
    [InlineData("do a deep dive on overdue tasks")]
    [InlineData("compare last week to the week before")]
    [InlineData("build me a personality snapshot from my journals")]
    [InlineData("generate insight about my workflow")]
    public void Classify_ReturnsHeavy_WhenInputContainsHeavyKeyword(string input)
    {
        Assert.Equal(TaskComplexity.Heavy, _classifier.Classify(input));
    }

    [Fact]
    public void Classify_HeavyKeywordMatching_IsCaseInsensitive()
    {
        Assert.Equal(TaskComplexity.Heavy, _classifier.Classify("PLEASE ANALYZE the data"));
    }

    [Fact]
    public void Classify_HeavyWinsOverLightLengthGate_WhenShortInputContainsKeyword()
    {
        Assert.Equal(TaskComplexity.Heavy, _classifier.Classify("compare"));
    }

    [Fact]
    public void Classify_HeavyWinsOverLightPrefixGate_WhenShowAnalyzeShortcut()
    {
        Assert.Equal(TaskComplexity.Heavy
                   , _classifier.Classify("show me the analysis of last quarter"));
    }

    // --------------------------------------------------------------
    // Light gate
    // --------------------------------------------------------------

    [Theory]
    [InlineData("hi")]
    [InlineData("hello there")]
    [InlineData("what time is it")]
    [InlineData("a short twenty five chars")]
    public void Classify_ReturnsLight_WhenInputIsShort(string input)
    {
        Assert.Equal(TaskComplexity.Light, _classifier.Classify(input));
    }

    [Theory]
    [InlineData("list all my open tasks for the current sprint")]
    [InlineData("show me everything I journalled about work this month")]
    [InlineData("health of all integrations including calendar and llm")]
    [InlineData("version of every running component please")]
    public void Classify_ReturnsLight_WhenInputStartsWithLightPrefix(string input)
    {
        Assert.Equal(TaskComplexity.Light, _classifier.Classify(input));
    }

    [Fact]
    public void Classify_LightPrefixMatching_IsCaseInsensitive()
    {
        Assert.Equal(TaskComplexity.Light
                   , _classifier.Classify("LIST every task tagged work please"));
    }

    [Fact]
    public void Classify_TrimsLeadingAndTrailingWhitespace_BeforeApplyingGates()
    {
        Assert.Equal(TaskComplexity.Light, _classifier.Classify("   hello   "));
    }

    // --------------------------------------------------------------
    // Standard default
    // --------------------------------------------------------------

    [Theory]
    [InlineData("schedule a meeting with the design team for next thursday morning")]
    [InlineData("remind me to take the dog out before the calendar event tomorrow")]
    public void Classify_ReturnsStandard_WhenInputIsLongAndHasNoHeavyOrLightSignal(string input)
    {
        Assert.Equal(TaskComplexity.Standard, _classifier.Classify(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Classify_ReturnsStandard_WhenInputIsNullOrWhitespace(string? input)
    {
        Assert.Equal(TaskComplexity.Standard, _classifier.Classify(input!));
    }
}
