namespace CognitivePlatform.Api.Domains.Journal.Import.Ttr;

public sealed class TtrImportItem
{
    public string                Id                     { get; init; } = string.Empty;
    public string                BatchId                { get; init; } = string.Empty;
    public long                  SourceEntryId          { get; init; }
    public long                  OriginalJournalId      { get; init; }
    public string                EntryId                { get; init; } = string.Empty;
    public string                RevisionId             { get; init; } = string.Empty;
    public string                SourceContentSha256    { get; init; } = string.Empty;
    public string                NormalizedContentSha256 { get; init; } = string.Empty;
    public string?               SourceJournalTitle     { get; init; }
    public string?               SourceJournalTypeTitle { get; init; }
    public string?               SourceMood             { get; init; }
    public IReadOnlyList<string> AttachmentIds          { get; init; } = [];
    public string                Status                 { get; set; } = string.Empty;
    public string?               Error                  { get; set; }
}
