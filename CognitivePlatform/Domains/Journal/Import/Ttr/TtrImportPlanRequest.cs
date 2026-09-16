namespace CognitivePlatform.Api.Domains.Journal.Import.Ttr;

public sealed class TtrImportPlanRequest
{
    public string DatabasePath           { get; init; } = string.Empty;
    public string RecoveryBundlePath     { get; init; } = string.Empty;
    public string ExpectedDatabaseSha256 { get; init; } = string.Empty;
    public string LogicalSourceInstance  { get; init; } = string.Empty;
    public string SourceTimeZoneId       { get; init; } = string.Empty;
    public string TargetPartition        { get; init; } = string.Empty;
}
