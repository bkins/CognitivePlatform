namespace CognitivePlatform.Api.Interpreter;

/// <summary>
/// Immutable snapshot of Groq rate-limit state captured from response headers.
/// Populated after every successful call to <see cref="GroqLlmClient"/>.
/// </summary>
public sealed class GroqUsageSnapshot
{
    // ----------------------------------------------------------------
    // Requests
    // ----------------------------------------------------------------

    public int     RequestLimit         { get; init; }
    public int     RequestsRemaining    { get; init; }

    /// <summary>Raw reset string as returned by Groq, e.g. "1m30s".</summary>
    public string  RequestsResetRaw     { get; init; } = string.Empty;

    /// <summary>Approximate local time when the request window resets.</summary>
    public DateTimeOffset? RequestsResetAt { get; init; }

    // ----------------------------------------------------------------
    // Tokens
    // ----------------------------------------------------------------

    public int     TokenLimit           { get; init; }
    public int     TokensRemaining      { get; init; }

    /// <summary>Raw reset string as returned by Groq, e.g. "2h".</summary>
    public string  TokensResetRaw       { get; init; } = string.Empty;

    /// <summary>Approximate local time when the token window resets.</summary>
    public DateTimeOffset? TokensResetAt   { get; init; }

    // ----------------------------------------------------------------
    // Meta
    // ----------------------------------------------------------------

    /// <summary>UTC timestamp when this snapshot was captured.</summary>
    public DateTimeOffset CapturedAt    { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>True if the snapshot contains at least one non-default value.</summary>
    public bool HasData => RequestLimit > 0 || TokenLimit > 0;

    // ----------------------------------------------------------------
    // Derived helpers (used by the controller response)
    // ----------------------------------------------------------------

    public int RequestsUsed  => RequestLimit - RequestsRemaining;
    public int TokensUsed    => TokenLimit   - TokensRemaining;

    public double RequestUsagePercent => RequestLimit > 0
                                                 ? Math.Round((double)RequestsUsed / RequestLimit * 100, 1)
                                                 : 0;

    public double TokenUsagePercent   => TokenLimit > 0
                                                 ? Math.Round((double)TokensUsed   / TokenLimit   * 100, 1)
                                                 : 0;

    public static readonly GroqUsageSnapshot Empty = new();
}