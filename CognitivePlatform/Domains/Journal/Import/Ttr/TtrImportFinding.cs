namespace CognitivePlatform.Api.Domains.Journal.Import.Ttr;

public sealed class TtrImportFinding
{
    public string Code        { get; init; } = string.Empty;
    public string Message     { get; init; } = string.Empty;
    public long? SourceEntryId { get; init; }
    public long? SourceMediaId { get; init; }
    public bool IsBlocking     { get; init; }
}
