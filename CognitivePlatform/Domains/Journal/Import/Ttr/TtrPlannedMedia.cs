namespace CognitivePlatform.Api.Domains.Journal.Import.Ttr;

public sealed class TtrPlannedMedia
{
    public long SourceEntryId    { get; init; }
    public long? SourceMediaId   { get; init; }
    public int? InlineMediaIndex { get; init; }
    public string AttachmentId   { get; init; } = string.Empty;
    public string LogicalFileName { get; init; } = string.Empty;
    public string ContentType     { get; init; } = string.Empty;
    public long SizeBytes         { get; init; }
    public string ContentSha256   { get; init; } = string.Empty;
    public string? ResolvedPath   { get; init; }
    public string Disposition     { get; set; } = "Ready";
}
