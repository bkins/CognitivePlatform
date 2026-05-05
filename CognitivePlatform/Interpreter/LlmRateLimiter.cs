using System.Collections.Concurrent;

namespace CognitivePlatform.Api.Interpreter;

/// <summary>
/// Thread-safe singleton implementation of <see cref="ILlmRateLimiter"/>.
/// Stores one snapshot per provider keyed by provider name (case-insensitive).
/// Volatile reference swap ensures readers never observe a partial write.
/// </summary>
public sealed class LlmRateLimiter : ILlmRateLimiter
{
    private readonly ConcurrentDictionary<string, LlmRateLimitSnapshot> _snapshots
        = new(StringComparer.OrdinalIgnoreCase);

    public void UpdateFromSnapshot(LlmRateLimitSnapshot snapshot)
    {
        if (snapshot.Provider.Length == 0)
            return;

        _snapshots[snapshot.Provider] = snapshot;
    }

    public bool CanSend(string provider)
    {
        if (!_snapshots.TryGetValue(provider, out var snapshot))
            return true; // optimistic: no data yet

        if (snapshot.HasData.Equals(false))
            return true;

        return snapshot.RequestsRemaining > 0 && snapshot.TokensRemaining > 0;
    }

    public LlmRateLimitSnapshot GetCurrentSnapshot(string provider)
    {
        return _snapshots.TryGetValue(provider, out var snapshot)
                       ? snapshot
                       : LlmRateLimitSnapshot.Empty;
    }
}
