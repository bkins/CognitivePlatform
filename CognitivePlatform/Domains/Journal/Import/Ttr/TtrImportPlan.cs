namespace CognitivePlatform.Api.Domains.Journal.Import.Ttr;

public sealed class TtrImportPlan
{
    public string SourceSha256          { get; init; } = string.Empty;
    public string SchemaFingerprint     { get; init; } = string.Empty;
    public string PlanSha256            { get; set; } = string.Empty;
    public string HumanReadableReport   { get; set; } = string.Empty;
    public string LogicalSourceInstance { get; init; } = string.Empty;
    public string SourceTimeZoneId      { get; init; } = string.Empty;
    public string TargetPartition       { get; init; } = string.Empty;
    public IReadOnlyList<TtrPlannedEntry> Entries      { get; init; } = [];
    public IReadOnlyList<TtrPlannedMedia> Media        { get; init; } = [];
    public IReadOnlyList<TtrImportFinding> Findings    { get; init; } = [];
    public IReadOnlyList<string> IgnoredFiles          { get; init; } = [];

    public int ReadyEntryCount   => Entries.Count(entry => entry.Disposition == "Ready");
    public int BlockedEntryCount => Entries.Count(entry => entry.Disposition == "Blocked");
}
