namespace CognitivePlatform.Api.Domains.Journal.Import.Ttr;

public sealed class TtrImportBatch
{
    public string          Id                   { get; init; } = string.Empty;
    public string          SourceSha256         { get; init; } = string.Empty;
    public string          PlanSha256           { get; init; } = string.Empty;
    public string          BackupManifestSha256 { get; init; } = string.Empty;
    public string          TargetPartition      { get; init; } = string.Empty;
    public DateTimeOffset  StartedUtc            { get; init; }
    public DateTimeOffset? CompletedUtc          { get; set; }
    public string          Status                { get; set; } = "Running";
    public int             ImportedCount         { get; set; }
    public int             AlreadyImportedCount  { get; set; }
    public int             FailedCount           { get; set; }
    public bool            VerificationPassed    { get; set; }
    public string          EmbeddingStatus       { get; set; } = "Pending";
    public int             EmbeddedCount         { get; set; }
    public int             EmbeddingFailedCount  { get; set; }
}
