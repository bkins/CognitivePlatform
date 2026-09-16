namespace CognitivePlatform.Api.Domains.Journal.Import.Ttr;

public sealed class TtrImportExecutionRequest
{
    public string ReviewedPlanSha256   { get; init; } = string.Empty;
    public string BackupManifestPath   { get; init; } = string.Empty;
    public string BackupManifestSha256 { get; init; } = string.Empty;
    public string TargetEnvironment    { get; init; } = string.Empty;
    public string TargetDatabasePath   { get; init; } = string.Empty;
}
