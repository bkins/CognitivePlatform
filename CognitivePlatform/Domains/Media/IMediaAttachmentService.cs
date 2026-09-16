namespace CognitivePlatform.Api.Domains.Media;

public interface IMediaAttachmentService
{
    Task<MediaAttachment>                AddAttachmentAsync      (string ownerType, string ownerId, string fileName, string contentType, Stream stream, long fileSizeBytes);
    Task<MediaAttachment>                AddImportedAttachmentAsync(string id, string ownerType, string ownerId, string fileName, string contentType, Stream stream, long fileSizeBytes, DateTimeOffset createdAt);
    Task<IReadOnlyList<MediaAttachment>> GetAttachmentsAsync     (string ownerType, string ownerId);
    Task<MediaAttachment?>               GetAttachmentAsync      (string id);
    Task<Stream?>                        GetAttachmentStreamAsync(string id);
    Task<bool>                           DeleteAttachmentAsync   (string id);
    Task<int>                            GetAttachmentCountAsync (string ownerType, string ownerId);
}
