namespace CognitivePlatform.Api.Domains.Media;

public sealed class MediaAttachmentDto
{
    public Guid           Id            { get; init; }
    public string         OwnerType     { get; init; } = string.Empty;
    public Guid           OwnerId       { get; init; }
    public string         FileName      { get; init; } = string.Empty;
    public string         ContentType   { get; init; } = string.Empty;
    public long           FileSizeBytes { get; init; }
    public DateTimeOffset CreatedAt     { get; init; }
    public string         StoragePath   { get; set; } = string.Empty;
}
