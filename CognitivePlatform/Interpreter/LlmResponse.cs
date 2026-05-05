namespace CognitivePlatform.Api.Interpreter;

/// <summary>
/// Enriched result returned by <see cref="ILlmClient.SendAsync"/>.
/// Wraps the raw text content together with per-response token usage and
/// the provider's current rate-limit state.
///
/// Consumers that only need the text can read <see cref="Content"/> directly.
/// The router and pipeline use <see cref="Usage"/> and <see cref="RateLimits"/>
/// to feed <see cref="ILlmUsageAggregator"/> and <see cref="ILlmRateLimiter"/>.
/// </summary>
public sealed class LlmResponse
{
    /// <summary>The raw text output produced by the model.</summary>
    public string Content { get; init; } = string.Empty;

    /// <summary>
    /// Per-response token counts extracted from the response body.
    /// Returns <see cref="LlmUsageInfo.Empty"/> when the provider did not
    /// include usage data.
    /// </summary>
    public LlmUsageInfo Usage { get; init; } = LlmUsageInfo.Empty;

    /// <summary>
    /// Rate-limit state captured from the provider's response headers.
    /// Returns <see cref="LlmRateLimitSnapshot.Empty"/> for providers that
    /// do not expose rate-limit headers (Gemini, Ollama, etc.).
    /// </summary>
    public LlmRateLimitSnapshot RateLimits { get; init; } = LlmRateLimitSnapshot.Empty;
}
