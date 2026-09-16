namespace CognitivePlatform.Api.Domains.Journal.Import.Ttr;

public sealed class TtrPlannedEntry
{
    public long SourceEntryId       { get; init; }
    public long OriginalJournalId   { get; init; }
    public string EntryId           { get; init; } = string.Empty;
    public string RevisionId        { get; init; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; init; }
    public string NormalizedText    { get; init; } = string.Empty;
    public string SourceContentSha256     { get; init; } = string.Empty;
    public string NormalizedContentSha256 { get; init; } = string.Empty;
    public IReadOnlyList<string> Tags     { get; init; } = [];
    public string? Mood                    { get; init; }
    public int? MoodScore                  { get; init; }
    public int? MoodLevel                  { get; init; }
    public string? SourceJournalTitle      { get; init; }
    public string? SourceJournalTypeTitle  { get; init; }
    public string Disposition              { get; set; } = "Ready";
}
