namespace CognitivePlatform.Admin.Services;

/// <summary>
/// Singleton in-memory ring-buffer for errors that originate inside the Admin
/// app itself (circuit exceptions, render failures, etc.).  These are separate
/// from the CP API logs shown via IAdminLogsClient.
/// </summary>
public sealed class AdminErrorLog
{
    private const int MaxEntries = 200;

    private readonly List<AdminErrorEntry> _entries = new();
    private readonly object                _lock    = new();

    /// <summary>Fired on the thread that called <see cref="Add"/> whenever a new entry is appended.</summary>
    public event Action? Changed;

    public void Add( string     level
                   , string     category
                   , string     message
                   , Exception? exception = null )
    {
        var entry = new AdminErrorEntry
        {
            TimestampUtc = DateTime.UtcNow
          , Level        = level
          , Category     = category
          , Message      = message
          , Details      = exception?.ToString()
        };

        lock (_lock)
        {
            _entries.Add(entry);
            if (_entries.Count > MaxEntries)
                _entries.RemoveAt(0);
        }

        Changed?.Invoke();
    }

    public IReadOnlyList<AdminErrorEntry> GetAll()
    {
        lock (_lock)
            return [.._entries];
    }

    public void Clear()
    {
        lock (_lock) _entries.Clear();
        Changed?.Invoke();
    }
}

public sealed record AdminErrorEntry
{
    public DateTime TimestampUtc { get; init; }
    public string   Level        { get; init; } = string.Empty;
    public string   Category     { get; init; } = string.Empty;
    public string   Message      { get; init; } = string.Empty;
    public string?  Details      { get; init; }
}
