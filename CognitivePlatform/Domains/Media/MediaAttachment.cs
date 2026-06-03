namespace CognitivePlatform.Api.Domains.Media;

public sealed class MediaAttachment
{
    public string          Id            { get; init; } = default!;
    public string          OwnerType     { get; init; } = default!;
    public string          OwnerId       { get; init; } = default!;
    public string          FileName      { get; init; } = default!;
    public string          ContentType   { get; init; } = default!;
    public long            FileSizeBytes { get; init; }
    public string          StoragePath   { get; init; } = default!;
    public DateTimeOffset  CreatedAt     { get; init; }
    public bool            IsDeleted     { get; set; }
    public DateTimeOffset? DeletedUtc    { get; set; }
}
