namespace CognitivePlatform.Api.Domains.Document;

public sealed record IndexedDocument
{
    public string   Id            { get; init; } = string.Empty;
    public string   FilePath      { get; init; } = string.Empty;
    public string   FileName      { get; init; } = string.Empty;
    public long     FileSizeBytes { get; init; }
    public int      ChunkCount    { get; init; }
    public DateTime IndexedAt     { get; init; } = DateTime.UtcNow;
    public DateTime LastModified  { get; init; }
}
