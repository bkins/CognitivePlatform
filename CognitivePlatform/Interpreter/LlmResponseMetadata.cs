namespace CognitivePlatform.Api.Interpreter;

/// <summary>
/// Structured metadata produced after each LLM call. Carries provider identity,
/// token usage, and rate-limit state as a single unit so subsystems receive
/// consistent, correlated data.
///
/// Used by <see cref="ILlmUsageAggregator"/> and <see cref="ILlmRateLimiter"/> as
/// their shared input type — avoids duplicate header parsing.
/// </summary>
public sealed class LlmResponseMetadata
{
    /// <summary>Provider that handled the request (e.g., "Groq").</summary>
    public string ProviderId { get; init; } = string.Empty;

    /// <summary>Model that was selected for the request.</summary>
    public string ModelId { get; init; } = string.Empty;

    /// <summary>Token usage reported in the response body.</summary>
    public LlmUsageInfo Usage { get; init; } = LlmUsageInfo.Empty;

    /// <summary>Rate-limit state captured from response headers.</summary>
    public LlmRateLimitSnapshot RateLimits { get; init; } = LlmRateLimitSnapshot.Empty;

    /// <summary>UTC timestamp when this metadata was captured.</summary>
    public DateTimeOffset CapturedUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Set when the capacity router switched to a fallback provider because the
    /// session-preferred provider was exhausted (ENH-12). Null in the normal path.
    /// Example: "Switched to gemini (gemini-2.0-flash) — Groq limit reached".
    /// </summary>
    public string? ProviderSwitchNote { get; init; }

    /// <summary>
    /// Set when the requested <see cref="TaskComplexity"/> was Heavy but the capacity router
    /// could not find an available Heavy-tier model and fell back to a Standard-tier model (ENH-19).
    /// Null when the model meets or exceeds the requested tier.
    /// </summary>
    public string? TierDowngradeNote { get; init; }
}
