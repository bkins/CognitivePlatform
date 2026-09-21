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

        await _store.Save(attachment, partitionKey: null, id: id);

        _logger.LogInformation("Media attachment {Id} saved for {OwnerType}/{OwnerId}"
                             , id, ownerType, ownerId);

        return attachment;
    }

    public async Task<MediaAttachment> AddImportedAttachmentAsync(string id
                                                                , string ownerType
                                                                , string ownerId
                                                                , string fileName
                                                                , string contentType
                                                                , Stream stream
                                                                , long fileSizeBytes
                                                                , DateTimeOffset createdAt)
    {
        if (!Guid.TryParse(id, out _)) throw new ArgumentException("Imported attachment ID must be a GUID.", nameof(id));
        if (_store is not IHistoricalObjectWriter historicalWriter)
            throw new InvalidOperationException("The configured object store does not support historical media writes.");
        var safeFileName = $"{id}-{SanitizeFileName(fileName)}";
        var storagePath  = Path.Combine(_mediaRoot, ownerType, ownerId, safeFileName);
        var existing     = _store.Get<MediaAttachment>(id, partitionKey: null);
        if (existing is not null)
        {
            if (existing.OwnerType == ownerType
             && existing.OwnerId == ownerId
             && existing.FileName == fileName
             && existing.ContentType == contentType
             && existing.FileSizeBytes == fileSizeBytes
             && existing.CreatedAt == createdAt
             && existing.StoragePath == storagePath
             && _fileStorage.Exists(storagePath))
                return existing;
            throw new InvalidOperationException($"Imported media identity collision for attachment {id}.");
        }

        var directory = Path.GetDirectoryName(storagePath)!;
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
                           , CreatedAt     = createdAt
                         };
        await historicalWriter.SaveHistorical(attachment, createdAt, partitionKey: null, id);
        return attachment;
    }

    public Task<IReadOnlyList<MediaAttachment>> GetAttachmentsAsync(string ownerType, string ownerId)
    {
        var attachments = _store.List<MediaAttachment>(partitionKey: null)
                                .Where(attachment => attachment.OwnerType == ownerType
                                                  && attachment.OwnerId == ownerId
                                                  && !attachment.IsDeleted)
                                .ToList();
        return Task.FromResult<IReadOnlyList<MediaAttachment>>(attachments);
    }

    public Task<MediaAttachment?> GetAttachmentAsync(string id)
    {
        var attachment = GetByCompatibleId(id);
        return Task.FromResult(attachment);
    }

    public Task<Stream?> GetAttachmentStreamAsync(string id)
    {
        var attachment = GetByCompatibleId(id);
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
        foreach (var candidate in GetCompatibleIds(id))
        {
            if (_store.SoftDelete<MediaAttachment>(candidate, partitionKey: null))
                return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    public Task<int> GetAttachmentCountAsync(string ownerType, string ownerId)
    {
        var count = _store.List<MediaAttachment>(partitionKey: null)
                          .Count(attachment => attachment.OwnerType == ownerType
                                            && attachment.OwnerId == ownerId
                                            && !attachment.IsDeleted);
        return Task.FromResult(count);
    }

    private static string BuildPartitionKey(string ownerType, string ownerId)
        => $"{ownerType}/{ownerId}";

    private MediaAttachment? GetByCompatibleId(string id)
    {
        foreach (var candidate in GetCompatibleIds(id))
        {
            var attachment = _store.Get<MediaAttachment>(candidate, partitionKey: null);
            if (attachment is not null)
                return attachment;
        }

        return null;
    }

    private static IEnumerable<string> GetCompatibleIds(string id)
    {
        yield return id;

        if (!Guid.TryParse(id, out var guid))
            yield break;

        var compact = guid.ToString("N");
        if (!compact.Equals(id, StringComparison.OrdinalIgnoreCase))
            yield return compact;

        var hyphenated = guid.ToString("D");
        if (!hyphenated.Equals(id, StringComparison.OrdinalIgnoreCase))
            yield return hyphenated;
    }

    private static string SanitizeFileName(string fileName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(fileName.Select(ch => invalid.Contains(ch) ? '_' : ch));
    }
}
