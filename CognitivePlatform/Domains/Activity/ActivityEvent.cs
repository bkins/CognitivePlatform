namespace CognitivePlatform.Api.Domains.Activity;

/// <summary>
/// A single immutable record of an activity or habit event — e.g. "ran for 30 minutes",
/// "read 40 pages", "meditated 10 minutes". Append-only; never mutated after creation.
///
/// ActivityType is free-text (lowercase-normalised, trimmed) rather than a closed enum
/// because the vocabulary is user-driven and still evolving.
/// </summary>
public sealed class ActivityEvent
{
    public string          Id           { get; init; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset  OccurredUtc  { get; init; } = DateTimeOffset.UtcNow;
    public string          ActivityType { get; init; } = string.Empty;

    /// <summary>Optional quantity — e.g. 30 minutes, 40 pages, 20 reps.</summary>
    public int?            Duration     { get; init; }

    /// <summary>Unit for <see cref="Duration"/> — e.g. "minutes", "pages", "reps".</summary>
    public string?         Unit         { get; init; }

    public string?         Notes        { get; init; }

    public List<string>    Tags         { get; init; } = new();

    /// <summary>
    /// Extensible bag for future fields. Mirrors the AuditEvent / TaskItem idiom.
    /// </summary>
    public Dictionary<string, string> Meta { get; init; } = new();
}
