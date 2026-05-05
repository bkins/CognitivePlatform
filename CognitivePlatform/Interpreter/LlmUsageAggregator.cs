namespace CognitivePlatform.Api.Interpreter;

/// <summary>
/// Thread-safe singleton implementation of <see cref="ILlmUsageAggregator"/>.
/// Uses <see cref="Interlocked.Add"/> for lock-free accumulation of token counts.
/// </summary>
public sealed class LlmUsageAggregator : ILlmUsageAggregator
{
    private long _promptTokens     = 0;
    private long _completionTokens = 0;
    private long _totalTokens      = 0;

    public void Record(LlmUsageInfo usage)
    {
        if (usage.PromptTokens == 0 && usage.CompletionTokens == 0)
            return;

        Interlocked.Add(ref _promptTokens,     usage.PromptTokens);
        Interlocked.Add(ref _completionTokens, usage.CompletionTokens);
        Interlocked.Add(ref _totalTokens,      usage.TotalTokens);
    }

    public LlmUsageInfo GetTotals()
    {
        return new LlmUsageInfo
               {
                       PromptTokens     = (int)Interlocked.Read(ref _promptTokens)
                     , CompletionTokens = (int)Interlocked.Read(ref _completionTokens)
                     , TotalTokens      = (int)Interlocked.Read(ref _totalTokens)
               };
    }
}
