namespace CognitivePlatform.Admin.Services;

public sealed class TerminalState
{
    private readonly List<TerminalLine> _lines = [];
    public List<TerminalLine> Lines => _lines;
    
    private readonly object             _lock  = new();

    public bool      IsRunning { get; internal set; }
    public int?      ExitCode  { get; internal set; }
    public string?   Error     { get; internal set; }
    public DateTime? StartedAt { get; internal set; }

    public string ElapsedDisplay
    {
        get
        {
            if (!IsRunning || !StartedAt.HasValue) return string.Empty;
            var elapsed = DateTime.UtcNow - StartedAt.Value;
            return $"Running {(int)elapsed.TotalMinutes}:{elapsed.Seconds:00}";
        }
    }

    internal void Append(TerminalLine line)
    {
        lock (_lock) { _lines.Add(line); }
    }

    public TerminalLine[] Snapshot()
    {
        lock (_lock) { return [.._lines]; }
    }

    internal void Reset()
    {
        lock (_lock) { _lines.Clear(); }
        IsRunning = false;
        ExitCode  = null;
        Error     = null;
        StartedAt = null;
    }
}
