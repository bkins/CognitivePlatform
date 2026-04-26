using CognitivePlatform.Api.Insights.Models;

namespace CognitivePlatform.Tests;

public class InsightTests
{
    [Fact]
    public void DefaultConstruction_HasEmptyMessageAndKey_AndDefaultPriorityCategory()
    {
        var insight = new Insight();

        Assert.Equal(string.Empty,         insight.Message);
        Assert.Equal(string.Empty,         insight.DeduplicationKey);
        Assert.Equal(InsightPriority.Normal, insight.Priority);
        Assert.Equal(InsightCategory.General, insight.Category);
        Assert.Null(insight.SuggestedAction);
        Assert.Null(insight.SuggestedParameters);
        Assert.Null(insight.Reasoning);
    }

    [Fact]
    public void RecordEquality_TreatsTwoInsightsWithSameScalars_AsEqual()
    {
        var first = new Insight
                    {
                            Message          = "hello"
                          , DeduplicationKey = "domain.signal"
                          , Priority         = InsightPriority.High
                          , Category         = InsightCategory.Reflection
                    };

        var second = new Insight
                     {
                             Message          = "hello"
                           , DeduplicationKey = "domain.signal"
                           , Priority         = InsightPriority.High
                           , Category         = InsightCategory.Reflection
                     };

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void RecordEquality_DistinguishesByDeduplicationKey()
    {
        var a = new Insight { Message = "hello", DeduplicationKey = "domain.signal-a" };
        var b = new Insight { Message = "hello", DeduplicationKey = "domain.signal-b" };

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void WithExpression_ProducesNewInstance_LeavingOriginalUntouched()
    {
        var original = new Insight
                       {
                               Message          = "original"
                             , DeduplicationKey = "key.one"
                             , Priority         = InsightPriority.Normal
                       };

        var elevated = original with { Priority = InsightPriority.High };

        Assert.Equal(InsightPriority.Normal, original.Priority);
        Assert.Equal(InsightPriority.High,   elevated.Priority);
        Assert.Equal(original.DeduplicationKey, elevated.DeduplicationKey);
    }
}
