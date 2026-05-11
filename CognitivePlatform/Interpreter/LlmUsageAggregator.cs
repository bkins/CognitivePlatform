namespace CognitivePlatform.Api.Interpreter;

/// <summary>
/// Thread-safe in-process implementation of <see cref="ILlmUsageAggregator"/>.
/// Uses <see cref="Interlocked.Add"/> for lock-free accumulation of token counts
/// and a lock-guarded list for the ordered call history.
/// </summary>
public sealed class InMemoryLlmUsageAggregator : ILlmUsageAggregator
{
    private long _promptTokens     = 0;
    private long _completionTokens = 0;
    private long _totalTokens      = 0;

    private readonly List<LlmResponseMetadata> _history = [];
    private readonly object                    _lock    = new();

    public void Record(LlmResponseMetadata metadata)
    {
        var usage = metadata.Usage;

        if (usage.PromptTokens == 0 && usage.CompletionTokens == 0)
            return;

        Interlocked.Add(ref _promptTokens,     usage.PromptTokens);
        Interlocked.Add(ref _completionTokens, usage.CompletionTokens);
        Interlocked.Add(ref _totalTokens,      usage.TotalTokens);

        lock (_lock)
            _history.Add(metadata);
    }

    public LlmUsageInfo GetSessionTotal()
    {
        return new LlmUsageInfo
               {
                       PromptTokens     = (int)Interlocked.Read(ref _promptTokens)
                     , CompletionTokens = (int)Interlocked.Read(ref _completionTokens)
                     , TotalTokens      = (int)Interlocked.Read(ref _totalTokens)
               };
    }

    public IReadOnlyList<LlmResponseMetadata> GetHistory()
    {
        lock (_lock)
            return _history.ToList();
    }
}
