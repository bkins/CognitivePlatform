namespace CognitivePlatform.Api.Integrations.FileSync.Models;

public sealed record SyncConflictRecord
{
    public string         Id             { get; init; } = string.Empty;
    public string         RelativePath   { get; init; } = string.Empty;
    public string         LocalBasePath  { get; init; } = string.Empty;
    public string         RemoteBasePath { get; init; } = string.Empty;
    public FileEntry      LocalEntry     { get; init; } = new();
    public FileEntry      RemoteEntry    { get; init; } = new();
    public DateTimeOffset RecordedAt     { get; init; }
}
