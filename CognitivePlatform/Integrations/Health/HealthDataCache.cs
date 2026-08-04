namespace CognitivePlatform.Api.Integrations.Health;

/// <summary>
/// Thread-safe in-memory store for health snapshots pushed from the LAA Android app.
/// Snapshots expire after <see cref="TtlMinutes"/> minutes to avoid serving stale data.
/// Keyed by <see cref="DateOnly"/> so each calendar date has exactly one current snapshot.
/// </summary>
public sealed class HealthDataCache
{
    private const int TtlMinutes = 60;

    private readonly Lock                                           _lock    = new();
    private readonly Dictionary<DateOnly, (HealthSnapshot Data, DateTime StoredAt)> _store = new();

    /// <summary>
    /// Stores or overwrites the snapshot for its date.
    /// </summary>
    public void Store(HealthSnapshot snapshot)
    {
        lock (_lock)
        {
            _store[snapshot.Date] = (snapshot, DateTime.UtcNow);
        }
    }

    /// <summary>
    /// Attempts to retrieve a fresh snapshot for <paramref name="date"/>.
    /// Returns <see langword="false"/> when no entry exists or when the entry has expired.
    /// </summary>
    public bool TryGet(DateOnly date, out HealthSnapshot? snapshot)
    {
        lock (_lock)
        {
            if (!_store.TryGetValue(date, out var entry))
            {
                snapshot = null;
                return false;
            }

            if ((DateTime.UtcNow - entry.StoredAt).TotalMinutes > TtlMinutes)
            {
                _store.Remove(date);
                snapshot = null;
                return false;
            }

            snapshot = entry.Data;
            return true;
        }
    }

    /// <summary>
    /// Returns the age of the snapshot for <paramref name="date"/>, or <see langword="null"/> if not cached.
    /// </summary>
    public TimeSpan? GetAge(DateOnly date)
    {
        lock (_lock)
        {
            if (_store.TryGetValue(date, out var entry))
                return DateTime.UtcNow - entry.StoredAt;

            return null;
        }
    }
}
