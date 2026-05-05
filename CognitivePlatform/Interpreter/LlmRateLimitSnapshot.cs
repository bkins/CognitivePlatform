namespace CognitivePlatform.Api.Interpreter;

/// <summary>
/// Immutable snapshot of provider rate-limit state. Provider-agnostic equivalent
/// of <see cref="GroqUsageSnapshot"/> — carries the same request/token window
/// data but is not tied to any single provider.
///
/// Groq: populated from response headers (x-ratelimit-*) after every call.
/// Gemini / OAI-compatible / Ollama: all fields remain at defaults (HasData = false)
/// until those providers expose rate-limit headers or body fields.
/// </summary>
public sealed class LlmRateLimitSnapshot
{
    /// <summary>The provider that produced this snapshot (e.g., "Groq").</summary>
    public string Provider { get; init; } = string.Empty;

    // ----------------------------------------------------------------
    // Requests
    // ----------------------------------------------------------------

    public int    RequestLimit      { get; init; }
    public int    RequestsRemaining { get; init; }

    /// <summary>Raw reset string as returned by the provider, e.g. "1m30s".</summary>
    public string RequestsResetRaw  { get; init; } = string.Empty;

    /// <summary>Approximate local time when the request window resets.</summary>
    public DateTimeOffset? RequestsResetAt { get; init; }

    // ----------------------------------------------------------------
    // Tokens
    // ----------------------------------------------------------------

    public int    TokenLimit      { get; init; }
    public int    TokensRemaining { get; init; }

    /// <summary>Raw reset string as returned by the provider, e.g. "2h".</summary>
    public string TokensResetRaw  { get; init; } = string.Empty;

    /// <summary>Approximate local time when the token window resets.</summary>
    public DateTimeOffset? TokensResetAt { get; init; }

    // ----------------------------------------------------------------
    // Meta
    // ----------------------------------------------------------------

    /// <summary>UTC timestamp when this snapshot was captured.</summary>
    public DateTimeOffset CapturedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>True if the snapshot contains at least one non-default value.</summary>
    public bool HasData => RequestLimit > 0 || TokenLimit > 0;

    // ----------------------------------------------------------------
    // Derived helpers
    // ----------------------------------------------------------------

    public int RequestsUsed  => RequestLimit - RequestsRemaining;
    public int TokensUsed    => TokenLimit   - TokensRemaining;

    public double RequestUsagePercent => RequestLimit > 0
                                              ? Math.Round((double)RequestsUsed / RequestLimit * 100, 1)
                                              : 0;

    public double TokenUsagePercent   => TokenLimit > 0
                                              ? Math.Round((double)TokensUsed   / TokenLimit   * 100, 1)
                                              : 0;

    public static readonly LlmRateLimitSnapshot Empty = new();
}
