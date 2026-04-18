using CP.Shared.Primitives.Avails.Extensions;

namespace CognitivePlatform.Api.Telemetry;

/// <summary>
/// Singleton ring-buffer of recent log entries captured by <see cref="InMemoryLogProvider"/>.
/// Capped at <see cref="MaxEntries"/>; oldest entries are evicted when full.
/// </summary>
public sealed class InMemoryLogStore
{
    private const int MaxEntries = 1_000;

    private readonly object          _lock    = new();
    private readonly List<LogEntry>  _entries = new(MaxEntries);

    public void Add(LogEntry entry)
    {
        lock (_lock)
        {
            _entries.Add(entry);
            if (_entries.Count > MaxEntries)
                _entries.RemoveAt(0);
        }
    }

    /// <summary>
    /// Returns up to <paramref name="take"/> entries, newest first.
    /// Optionally filtered by level abbreviation (INF, WRN, ERR …) and/or a search string.
    /// </summary>
    public IReadOnlyList<LogEntry> GetRecent( int     take   = 200
                                            , string? level  = null
                                            , string? search = null)
    {
        List<LogEntry> snapshot;
        lock (_lock)
            snapshot = [.._entries];

        IEnumerable<LogEntry> query = Enumerable.Reverse(snapshot);

        if (string.IsNullOrWhiteSpace(level).Not())
            query = query.Where(entry => entry.Level.Equals(level, StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(search).Not())
            query = query.Where(entry => entry.Message.Contains(search!, StringComparison.OrdinalIgnoreCase)
                                      || entry.Category.Contains(search!, StringComparison.OrdinalIgnoreCase));

        return query.Take(take).ToList();
    }

    public void Clear()
    {
        lock (_lock)
            _entries.Clear();
    }
}
