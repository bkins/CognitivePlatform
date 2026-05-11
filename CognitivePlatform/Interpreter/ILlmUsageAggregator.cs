namespace CognitivePlatform.Api.Interpreter;

/// <summary>
/// Accumulates token usage across all LLM calls within the lifetime of the
/// application process. Useful for surfacing cumulative cost/quota data in
/// admin endpoints and the LAA status badge.
///
/// Implementations must be thread-safe; the router calls <see cref="Record"/>
/// from concurrent request threads.
/// </summary>
public interface ILlmUsageAggregator
{
    /// <summary>
    /// Records usage from a single completed LLM call.
    /// No-ops when the metadata usage is all zeros.
    /// </summary>
    void Record(LlmResponseMetadata metadata);

    /// <summary>
    /// Returns a snapshot of the cumulative token totals since process start.
    /// The returned instance is a value snapshot — it will not change as
    /// further calls are recorded.
    /// </summary>
    LlmUsageInfo GetSessionTotal();

    /// <summary>
    /// Returns the ordered history of all recorded metadata entries since process start.
    /// </summary>
    IReadOnlyList<LlmResponseMetadata> GetHistory();
}
