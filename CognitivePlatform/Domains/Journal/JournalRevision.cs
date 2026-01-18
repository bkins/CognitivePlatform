namespace CognitivePlatform.Api.Domains.Journal;

public sealed class JournalRevision
{
    public string RevisionId { get; init; } = default!;
    public string EntryId    { get; init; } = default!;

    public DateTimeOffset CreatedUtc { get; init; }

    public string                Text       { get; init; } = string.Empty;
    public IReadOnlyList<string> Tags       { get; init; } = Array.Empty<string>();
    public string?               Mood       { get; init; }
    public int?                  MoodScore  { get; init; }
    public int?                  MoodLevel  { get; init; }
    public IReadOnlyList<string> MediaPaths { get; init; } = Array.Empty<string>();
    public JournalEntryState     State      { get; set; }
}
