using System.Collections.Concurrent;

namespace CognitivePlatform.Api.Interpreter;

/// <summary>
/// Thread-safe in-process implementation of <see cref="ILlmRateLimiter"/>.
/// Stores one snapshot per provider keyed by provider name (case-insensitive).
/// </summary>
public sealed class InMemoryLlmRateLimiter : ILlmRateLimiter
{
    private readonly ConcurrentDictionary<string, LlmRateLimitSnapshot> _snapshots
        = new(StringComparer.OrdinalIgnoreCase);

    public void Update(LlmResponseMetadata metadata)
    {
        if (metadata.ProviderId.Length == 0)
            return;

        _snapshots[metadata.ProviderId] = metadata.RateLimits;
    }

    public bool IsExhausted(string provider)
    {
        if (!_snapshots.TryGetValue(provider, out var snapshot))
            return false; // optimistic: no data yet

        if (!snapshot.HasData)
            return false;

        return snapshot.RequestsRemaining == 0 || snapshot.TokensRemaining == 0;
    }

    public LlmRateLimitSnapshot GetLatest(string provider)
    {
        return _snapshots.TryGetValue(provider, out var snapshot)
                       ? snapshot
                       : LlmRateLimitSnapshot.Empty;
    }
}
