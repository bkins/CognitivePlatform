namespace CognitivePlatform.Api.Interpreter;

/// <summary>
/// Tracks the most recent rate-limit state for each LLM provider.
/// Updated by the router after every response (including 429s from the client).
///
/// Phase A: records snapshots from response headers (Groq only).
/// Phase B: will use this to drive pre-call throttling and provider fallback.
/// </summary>
public interface ILlmRateLimiter
{
    /// <summary>
    /// Records the latest rate-limit state from an LLM response.
    /// Thread-safe; called by the router after every send (success or 429).
    /// </summary>
    void Update(LlmResponseMetadata metadata);

    /// <summary>
    /// Returns <c>true</c> when the most recent snapshot for
    /// <paramref name="provider"/> shows requests or tokens are exhausted.
    /// Returns <c>false</c> (not exhausted) when no snapshot has been recorded
    /// yet — optimistic default so the first request is always attempted.
    /// </summary>
    bool IsExhausted(string provider);

    /// <summary>
    /// Returns the most recent snapshot for <paramref name="provider"/>,
    /// or <see cref="LlmRateLimitSnapshot.Empty"/> if none has been recorded.
    /// </summary>
    LlmRateLimitSnapshot GetLatest(string provider);

    /// <summary>
    /// Alias for <see cref="GetLatest"/>. Returns the current rate-limit snapshot
    /// for <paramref name="provider"/>, or <see cref="LlmRateLimitSnapshot.Empty"/> if
    /// no snapshot has been recorded yet.
    /// </summary>
    LlmRateLimitSnapshot GetCurrentSnapshot(string provider);
}
