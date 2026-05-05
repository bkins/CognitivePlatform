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
    /// Records the token counts from a single completed LLM call.
    /// No-ops when <paramref name="usage"/> is <see cref="LlmUsageInfo.Empty"/>
    /// or all counts are zero.
    /// </summary>
    void Record(LlmUsageInfo usage);

    /// <summary>
    /// Returns a snapshot of the cumulative totals since process start.
    /// The returned instance is a value snapshot — it will not change as
    /// further calls are recorded.
    /// </summary>
    LlmUsageInfo GetTotals();
}
