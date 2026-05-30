namespace CognitivePlatform.Api.Integrations.FileSync.Models;

public sealed record FileEntry
{
    public string         RelativePath { get; init; } = string.Empty;
    public long           SizeBytes    { get; init; }
    public DateTimeOffset LastModified { get; init; }
    public string?        ContentHash  { get; init; }
}
