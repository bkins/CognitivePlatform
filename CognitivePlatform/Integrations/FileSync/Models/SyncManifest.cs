namespace CognitivePlatform.Api.Integrations.FileSync.Models;

public sealed record SyncManifest
{
    public string                   ManifestId      { get; init; } = string.Empty;
    public string                   SourcePath      { get; init; } = string.Empty;
    public string                   DestinationPath { get; init; } = string.Empty;
    public DateTimeOffset           LastSyncedAt    { get; init; }
    public IReadOnlyList<FileEntry> SnapshotAtSync  { get; init; } = [];
}
