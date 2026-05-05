using CognitivePlatform.Api.Interpreter;

namespace CognitivePlatform.Tests;

public class LlmUsageAggregatorTests
{
    private readonly LlmUsageAggregator _aggregator = new();

    // ----------------------------------------------------------------
    // Record / GetTotals
    // ----------------------------------------------------------------

    [Fact]
    public void GetTotals_ReturnsZeros_BeforeAnyCallIsRecorded()
    {
        var totals = _aggregator.GetTotals();

        Assert.Equal(0, totals.PromptTokens);
        Assert.Equal(0, totals.CompletionTokens);
        Assert.Equal(0, totals.TotalTokens);
    }

    [Fact]
    public void Record_AccumulatesTokenCounts_AcrossMultipleCalls()
    {
        _aggregator.Record(new LlmUsageInfo { PromptTokens = 10, CompletionTokens = 5,  TotalTokens = 15 });
        _aggregator.Record(new LlmUsageInfo { PromptTokens = 20, CompletionTokens = 10, TotalTokens = 30 });

        var totals = _aggregator.GetTotals();

        Assert.Equal(30, totals.PromptTokens);
        Assert.Equal(15, totals.CompletionTokens);
        Assert.Equal(45, totals.TotalTokens);
    }

    [Fact]
    public void Record_IsNoOp_WhenAllCountsAreZero()
    {
        _aggregator.Record(LlmUsageInfo.Empty);

        var totals = _aggregator.GetTotals();

        Assert.Equal(0, totals.PromptTokens);
        Assert.Equal(0, totals.CompletionTokens);
        Assert.Equal(0, totals.TotalTokens);
    }

    [Fact]
    public void GetTotals_ReturnsSnapshot_NotLiveReference()
    {
        _aggregator.Record(new LlmUsageInfo { PromptTokens = 10, CompletionTokens = 5, TotalTokens = 15 });
        var snapshot = _aggregator.GetTotals();

        // Record more AFTER taking snapshot
        _aggregator.Record(new LlmUsageInfo { PromptTokens = 100, CompletionTokens = 50, TotalTokens = 150 });

        // Original snapshot should be unchanged
        Assert.Equal(10, snapshot.PromptTokens);
    }

    [Fact]
    public async Task Record_IsThreadSafe_UnderConcurrentLoad()
    {
        var tasks = Enumerable.Range(0, 100)
                              .Select(_ => Task.Run(() =>
                                  _aggregator.Record(new LlmUsageInfo
                                                     {
                                                             PromptTokens     = 1
                                                           , CompletionTokens = 1
                                                           , TotalTokens      = 2
                                                     })))
                              .ToArray();

        await Task.WhenAll(tasks);

        var totals = _aggregator.GetTotals();

        Assert.Equal(100, totals.PromptTokens);
        Assert.Equal(100, totals.CompletionTokens);
        Assert.Equal(200, totals.TotalTokens);
    }
}
