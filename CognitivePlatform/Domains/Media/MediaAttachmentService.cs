using CognitivePlatform.Api.Data;
using Microsoft.Extensions.Options;

namespace CognitivePlatform.Api.Domains.Media;

public sealed class MediaAttachmentService : IMediaAttachmentService
{
    private readonly IObjectStore                    _store;
    private readonly IMediaFileStorage               _fileStorage;
    private readonly string                          _mediaRoot;
    private readonly ILogger<MediaAttachmentService> _logger;

    public MediaAttachmentService(IObjectStore                       store
                                , IMediaFileStorage                  fileStorage
                                , IOptions<MediaAttachmentSettings>  settings
                                , ILogger<MediaAttachmentService>    logger)
    {
        _store       = store;
        _fileStorage = fileStorage;
        _mediaRoot   = settings.Value.MediaRootPath;
        _logger      = logger;
    }

    public async Task<MediaAttachment> AddAttachmentAsync(string ownerType
                                                        , string ownerId
                                                        , string fileName
                                                        , string contentType
                                                        , Stream stream
                                                        , long   fileSizeBytes)
    {
        var id           = Guid.NewGuid().ToString("N");
        var safeFileName = SanitizeFileName(fileName);
        var storagePath  = Path.Combine(_mediaRoot, ownerType, ownerId, safeFileName);
        var directory    = Path.GetDirectoryName(storagePath)!;

        _fileStorage.EnsureDirectory(directory);
        await _fileStorage.WriteAsync(storagePath, stream);

        var attachment = new MediaAttachment
                         {
                             Id            = id
                           , OwnerType     = ownerType
                           , OwnerId       = ownerId
                           , FileName      = fileName
                           , ContentType   = contentType
                           , FileSizeBytes = fileSizeBytes
                           , StoragePath   = storagePath
                           , CreatedAt     = DateTimeOffset.UtcNow
                         };

        await _store.Save(attachment, BuildPartitionKey(ownerType, ownerId), id);

        _logger.LogInformation("Media attachment {Id} saved for {OwnerType}/{OwnerId}"
                             , id, ownerType, ownerId);

        return attachment;
    }

    public Task<IReadOnlyList<MediaAttachment>> GetAttachmentsAsync(string ownerType, string ownerId)
    {
        var attachments = _store.List<MediaAttachment>(BuildPartitionKey(ownerType, ownerId));
        return Task.FromResult(attachments);
    }

    public Task<MediaAttachment?> GetAttachmentAsync(string id)
    {
        var attachment = _store.Get<MediaAttachment>(id);
        return Task.FromResult(attachment);
    }

    public Task<Stream?> GetAttachmentStreamAsync(string id)
    {
        var attachment = _store.Get<MediaAttachment>(id);
        if (attachment is null || attachment.IsDeleted)
            return Task.FromResult<Stream?>(null);

        if (!_fileStorage.Exists(attachment.StoragePath))
        {
            _logger.LogWarning("Media file missing for attachment {Id} at {Path}"
                             , id, attachment.StoragePath);
            return Task.FromResult<Stream?>(null);
        }

        Stream stream = _fileStorage.OpenRead(attachment.StoragePath);
        return Task.FromResult<Stream?>(stream);
    }

    public Task<bool> DeleteAttachmentAsync(string id)
    {
        var deleted = _store.SoftDelete<MediaAttachment>(id);
        return Task.FromResult(deleted);
    }

    public Task<int> GetAttachmentCountAsync(string ownerType, string ownerId)
    {
        var count = _store.List<MediaAttachment>(BuildPartitionKey(ownerType, ownerId)).Count;
        return Task.FromResult(count);
    }

    private static string BuildPartitionKey(string ownerType, string ownerId)
        => $"{ownerType}/{ownerId}";

    private static string SanitizeFileName(string fileName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(fileName.Select(ch => invalid.Contains(ch) ? '_' : ch));
    }
}
