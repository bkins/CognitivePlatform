namespace CognitivePlatform.Api.Interpreter;

/// <summary>
/// Tracks the most recent rate-limit state for each LLM provider and
/// exposes a <see cref="CanSend"/> guard that callers can check before
/// dispatching a request.
///
/// Phase A records snapshots from completed responses (header-based, Groq only).
/// Phase B will use this to drive pre-call throttling and provider fallback.
/// </summary>
public interface ILlmRateLimiter
{
    /// <summary>
    /// Records the latest rate-limit snapshot returned with an LLM response.
    /// Thread-safe; called by the router after every successful send.
    /// </summary>
    void UpdateFromSnapshot(LlmRateLimitSnapshot snapshot);

    /// <summary>
    /// Returns <c>true</c> when the most recent snapshot for
    /// <paramref name="provider"/> indicates capacity is available, or when
    /// no snapshot has been recorded yet (optimistic default).
    ///
    /// "Capacity available" means: requests remaining > 0 AND tokens remaining > 0,
    /// or <see cref="LlmRateLimitSnapshot.HasData"/> is false (no data yet).
    /// </summary>
    bool CanSend(string provider);

    /// <summary>
    /// Returns the most recent snapshot for <paramref name="provider"/>,
    /// or <see cref="LlmRateLimitSnapshot.Empty"/> if none has been recorded.
    /// </summary>
    LlmRateLimitSnapshot GetCurrentSnapshot(string provider);
}
