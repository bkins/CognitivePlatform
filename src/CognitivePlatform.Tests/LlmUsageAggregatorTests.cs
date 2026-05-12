using CognitivePlatform.Api.Interpreter;

namespace CognitivePlatform.Tests;

public class LlmUsageAggregatorTests
{
    private readonly InMemoryLlmUsageAggregator _aggregator = new();

    private static LlmResponseMetadata MetadataWith(int prompt, int completion, int total, string provider = "Groq")
        => new()
           {
                   ProviderId = provider
                 , Usage      = new LlmUsageInfo
                                {
                                        PromptTokens     = prompt
                                      , CompletionTokens = completion
                                      , TotalTokens      = total
                                }
           };

    // ----------------------------------------------------------------
    // Record / GetSessionTotal
    // ----------------------------------------------------------------

    [Fact]
    public void GetSessionTotal_ReturnsZeros_BeforeAnyCallIsRecorded()
    {
        var totals = _aggregator.GetSessionTotal();

        Assert.Equal(0, totals.PromptTokens);
        Assert.Equal(0, totals.CompletionTokens);
        Assert.Equal(0, totals.TotalTokens);
    }

    [Fact]
    public void Record_AccumulatesTokenCounts_AcrossMultipleCalls()
    {
        _aggregator.Record(MetadataWith(prompt: 10, completion: 5,  total: 15));
        _aggregator.Record(MetadataWith(prompt: 20, completion: 10, total: 30));

        var totals = _aggregator.GetSessionTotal();

        Assert.Equal(30, totals.PromptTokens);
        Assert.Equal(15, totals.CompletionTokens);
        Assert.Equal(45, totals.TotalTokens);
    }

    [Fact]
    public void Record_IsNoOp_WhenAllCountsAreZero()
    {
        _aggregator.Record(new LlmResponseMetadata { Usage = LlmUsageInfo.Empty });

        var totals = _aggregator.GetSessionTotal();

        Assert.Equal(0, totals.PromptTokens);
        Assert.Equal(0, totals.CompletionTokens);
        Assert.Equal(0, totals.TotalTokens);
    }

    [Fact]
    public void GetSessionTotal_ReturnsSnapshot_NotLiveReference()
    {
        _aggregator.Record(MetadataWith(prompt: 10, completion: 5, total: 15));
        var snapshot = _aggregator.GetSessionTotal();

        _aggregator.Record(MetadataWith(prompt: 100, completion: 50, total: 150));

        Assert.Equal(10, snapshot.PromptTokens);
    }

    [Fact]
    public async Task Record_IsThreadSafe_UnderConcurrentLoad()
    {
        var tasks = Enumerable.Range(0, 100)
                              .Select(_ => Task.Run(() =>
                                  _aggregator.Record(MetadataWith(prompt: 1, completion: 1, total: 2))))
                              .ToArray();

        await Task.WhenAll(tasks);

        var totals = _aggregator.GetSessionTotal();

        Assert.Equal(100, totals.PromptTokens);
        Assert.Equal(100, totals.CompletionTokens);
        Assert.Equal(200, totals.TotalTokens);
    }

    // ----------------------------------------------------------------
    // GetHistory
    // ----------------------------------------------------------------

    [Fact]
    public void GetHistory_ReturnsEmpty_BeforeAnyCallIsRecorded()
    {
        var history = _aggregator.GetHistory();

        Assert.Empty(history);
    }

    [Fact]
    public void GetHistory_ContainsEachRecordedMetadata_InOrder()
    {
        var first  = MetadataWith(prompt: 10, completion: 5,  total: 15, provider: "Groq");
        var second = MetadataWith(prompt: 20, completion: 10, total: 30, provider: "OpenRouter");

        _aggregator.Record(first);
        _aggregator.Record(second);

        var history = _aggregator.GetHistory();

        Assert.Equal(2,            history.Count);
        Assert.Equal("Groq",       history[0].ProviderId);
        Assert.Equal("OpenRouter", history[1].ProviderId);
    }

    [Fact]
    public void GetHistory_ReturnsSnapshot_NotLiveList()
    {
        _aggregator.Record(MetadataWith(prompt: 10, completion: 5, total: 15));
        var history = _aggregator.GetHistory();

        _aggregator.Record(MetadataWith(prompt: 20, completion: 10, total: 30));

        Assert.Single(history);
    }

    [Fact]
    public void GetHistory_IsEmpty_WhenRecordHasZeroUsage()
    {
        _aggregator.Record(new LlmResponseMetadata { ProviderId = "Groq", Usage = LlmUsageInfo.Empty });

        var history = _aggregator.GetHistory();

        Assert.Empty(history);
    }
}
