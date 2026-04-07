namespace CognitivePlatform.Api.Interpreter;

/// <summary>
/// Thread-safe singleton implementation of <see cref="IGroqUsageTracker"/>.
/// Uses an atomic reference swap so readers never observe a partial write.
/// </summary>
public sealed class GroqUsageTracker : IGroqUsageTracker
{
    private volatile GroqUsageSnapshot _current = GroqUsageSnapshot.Empty;

    public GroqUsageSnapshot Current => _current;

    public void Update(GroqUsageSnapshot snapshot)
    {
        // Volatile write — no lock needed for a single reference assignment.
        _current = snapshot;
    }
}