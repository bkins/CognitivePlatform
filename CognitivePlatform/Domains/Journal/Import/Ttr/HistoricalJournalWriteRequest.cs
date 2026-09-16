namespace CognitivePlatform.Api.Domains.Journal.Import.Ttr;

public sealed class HistoricalJournalWriteRequest
{
    public string                EntryId      { get; init; } = string.Empty;
    public string                RevisionId   { get; init; } = string.Empty;
    public DateTimeOffset        CreatedUtc   { get; init; }
    public string                PartitionKey { get; init; } = string.Empty;
    public string                Text         { get; init; } = string.Empty;
    public IReadOnlyList<string> Tags         { get; init; } = [];
    public string?               Mood         { get; init; }
    public int?                  MoodScore    { get; init; }
    public int?                  MoodLevel    { get; init; }
    public IReadOnlyList<string> MediaPaths   { get; init; } = [];
}
