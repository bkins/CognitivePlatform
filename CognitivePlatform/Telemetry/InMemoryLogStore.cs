using CP.Shared.Primitives.Avails.Extensions;

namespace CognitivePlatform.Api.Telemetry;

/// <summary>
/// Singleton ring-buffer of recent log entries captured by <see cref="InMemoryLogProvider"/>.
/// Capped at <see cref="MaxEntries"/>; oldest entries are evicted when full.
/// </summary>
public sealed class InMemoryLogStore
{
    private const int MaxEntries = 1_000;

    private readonly object           _lock    = new();
    private readonly Queue<LogEntry>  _entries = new(MaxEntries);

    public void Add(LogEntry entry)
    {
        lock (_lock)
        {
            _entries.Enqueue(entry);
            if (_entries.Count > MaxEntries)
            {
                _entries.Dequeue();
            }
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
        {
            snapshot = [.._entries];
        }

        var query = snapshot.AsEnumerable().Reverse();

        if (level.HasValue())
        {
            query = query.Where(entry => entry.Level.EqualsIgnoreCase(level));
        }

        if (search.HasValue())
        {
            query = query.Where(entry => entry.Message.ContainsIgnoreCase(search)
                                      || entry.Category.ContainsIgnoreCase(search));
        }

        return query.Take(take).ToList();
    }

    public void Clear()
    {
        lock (_lock)
        {
            _entries.Clear();
        }
    }
}
