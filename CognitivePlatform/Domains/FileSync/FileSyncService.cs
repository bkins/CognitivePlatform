using System.Security.Cryptography;
using System.Text;
using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Domains.Document;
using CognitivePlatform.Api.Integrations.FileSync;
using CognitivePlatform.Api.Integrations.FileSync.Models;
using Microsoft.Extensions.Logging;

namespace CognitivePlatform.Api.Domains.FileSync;

public sealed class FileSyncService : IFileSyncService
{
    private const string ManifestPartition = "file_sync_manifest";
    private const string ConflictPartition = "file_sync_conflict";

    private readonly IFileSyncProvider         _phoneProvider;
    private readonly ILocalFileSystem          _localFileSystem;
    private readonly IObjectStore              _objectStore;
    private readonly IDocumentIndexingService? _documentIndexingService;
    private readonly ILogger<FileSyncService>  _logger;

    public FileSyncService( IFileSyncProvider         phoneProvider
                          , ILocalFileSystem           localFileSystem
                          , IObjectStore               objectStore
                          , ILogger<FileSyncService>   logger
                          , IDocumentIndexingService?  documentIndexingService = null )
    {
        _phoneProvider           = phoneProvider           ?? throw new ArgumentNullException(nameof(phoneProvider));
        _localFileSystem         = localFileSystem         ?? throw new ArgumentNullException(nameof(localFileSystem));
        _objectStore             = objectStore             ?? throw new ArgumentNullException(nameof(objectStore));
        _logger                  = logger                  ?? throw new ArgumentNullException(nameof(logger));
        _documentIndexingService = documentIndexingService;
    }

    // -----------------------------------------------------------------------
    // GetManifestAsync — snapshot of local state with MD5 hashes
    // -----------------------------------------------------------------------

    public async Task<SyncManifest> GetManifestAsync(string path, CancellationToken ct = default)
    {
        var files           = await _localFileSystem.ListAsync(path, ct);
        var entriesWithHash = new List<FileEntry>(files.Count);

        foreach (var entry in files)
        {
            ct.ThrowIfCancellationRequested();

            var fullPath = Path.Combine(path, entry.RelativePath);
            var hash     = await ComputeMd5Async(fullPath, ct);

            entriesWithHash.Add(entry with { ContentHash = hash });
        }

        return new SyncManifest
               {
                   ManifestId      = ComputeManifestId(path, string.Empty)
                 , SourcePath      = path
                 , DestinationPath = string.Empty
                 , LastSyncedAt    = DateTimeOffset.UtcNow
                 , SnapshotAtSync  = entriesWithHash
               };
    }

    // -----------------------------------------------------------------------
    // PreviewSyncAsync — dry-run: counts what would move, no I/O
    // -----------------------------------------------------------------------

    public async Task<SyncResult> PreviewSyncAsync(string localPath, string remotePath, CancellationToken ct = default)
    {
        var localFiles  = await _localFileSystem.ListAsync(localPath, ct);
        var remoteFiles = await _phoneProvider.ListFilesAsync(remotePath, ct);
        var manifestId  = ComputeManifestId(localPath, remotePath);
        var stored      = _objectStore.Get<SyncManifest>(manifestId, ManifestPartition);
        var snapshot    = stored?.SnapshotAtSync ?? [];

        var delta = ComputeDelta(localFiles, remoteFiles, snapshot);

        var summary = BuildPreviewSummary( delta.ToUpload.Count + delta.ToDownload.Count
                                         , delta.Skipped
                                         , delta.Conflicts.Count );

        return new SyncResult
               {
                   FilesCopied  = delta.ToUpload.Count + delta.ToDownload.Count
                 , FilesSkipped = delta.Skipped
                 , Conflicts    = delta.Conflicts
                 , Summary      = summary
               };
    }

    // -----------------------------------------------------------------------
    // SyncFolderAsync — full sync: execute transfers, commit manifest
    // -----------------------------------------------------------------------

    public async Task<SyncResult> SyncFolderAsync(string localPath, string remotePath, CancellationToken ct = default)
    {
        var filesCopied  = 0;
        var filesSkipped = 0;
        var conflicts    = new List<FileConflict>();

        try
        {
            var localFiles  = await _localFileSystem.ListAsync(localPath, ct);
            var remoteFiles = await _phoneProvider.ListFilesAsync(remotePath, ct);
            var manifestId  = ComputeManifestId(localPath, remotePath);
            var stored      = _objectStore.Get<SyncManifest>(manifestId, ManifestPartition);
            var snapshot    = stored?.SnapshotAtSync ?? [];

            var delta = ComputeDelta(localFiles, remoteFiles, snapshot);
            filesSkipped = delta.Skipped;
            conflicts.AddRange(delta.Conflicts);

            // Persist each conflict record so ResolveSyncConflictAsync can act on it
            var localByPath  = localFiles.ToDictionary(file => file.RelativePath, StringComparer.OrdinalIgnoreCase);
            var remoteByPath = remoteFiles.ToDictionary(file => file.RelativePath, StringComparer.OrdinalIgnoreCase);

            foreach (var conflict in delta.Conflicts)
            {
                localByPath.TryGetValue(conflict.RelativePath, out var localEntry);
                remoteByPath.TryGetValue(conflict.RelativePath, out var remoteEntry);

                if (localEntry is not null && remoteEntry is not null)
                    await StoreConflictRecordAsync(conflict.RelativePath, localPath, remotePath, localEntry, remoteEntry, ct);
            }

            foreach (var relativePath in delta.ToUpload)
            {
                ct.ThrowIfCancellationRequested();
                await UploadFileAsync(localPath, relativePath, remotePath, ct);
                filesCopied++;
            }

            foreach (var relativePath in delta.ToDownload)
            {
                ct.ThrowIfCancellationRequested();
                await DownloadFileAsync(remotePath, relativePath, localPath, ct);
                filesCopied++;
            }

            // All transfers completed — commit the manifest as the new baseline
            var newManifest = new SyncManifest
                              {
                                  ManifestId      = manifestId
                                , SourcePath      = localPath
                                , DestinationPath = remotePath
                                , LastSyncedAt    = DateTimeOffset.UtcNow
                                , SnapshotAtSync  = localFiles
                              };
            await _objectStore.Save(newManifest, ManifestPartition, manifestId);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning( ex
                              , "File sync to '{RemotePath}' interrupted after {FilesCopied} file(s)"
                              , remotePath
                              , filesCopied );

            return new SyncResult
                   {
                       FilesCopied  = filesCopied
                     , FilesSkipped = filesSkipped
                     , Conflicts    = conflicts
                     , Summary      = $"Sync interrupted after {filesCopied} file{(filesCopied == 1 ? "" : "s")} — phone may have gone offline. Manifest not updated."
                   };
        }

        return new SyncResult
               {
                   FilesCopied  = filesCopied
                 , FilesSkipped = filesSkipped
                 , Conflicts    = conflicts
                 , Summary      = BuildSyncSummary(filesCopied, filesSkipped, conflicts.Count)
               };
    }

    // -----------------------------------------------------------------------
    // ResolveSyncConflictAsync — apply user's choice for a flagged conflict
    // -----------------------------------------------------------------------

    public async Task<string> ResolveSyncConflictAsync(string relativePath, string resolution, CancellationToken ct = default)
    {
        var record = _objectStore.Get<SyncConflictRecord>(relativePath, ConflictPartition);
        if (record is null)
            return $"No pending conflict found for '{relativePath}'. Run a sync first to detect conflicts.";

        var normalised = resolution.Trim().ToLowerInvariant();

        if (normalised == "newest")
            normalised = record.LocalEntry.LastModified >= record.RemoteEntry.LastModified
                             ? "laptop"
                             : "phone";

        switch (normalised)
        {
            case "laptop":
                await UploadFileAsync(record.LocalBasePath, relativePath, record.RemoteBasePath, ct);
                break;
            case "phone":
                await DownloadFileAsync(record.RemoteBasePath, relativePath, record.LocalBasePath, ct);
                break;
            default:
                return $"Unknown resolution '{resolution}'. Use 'laptop', 'phone', or 'newest'.";
        }

        _objectStore.SoftDelete<SyncConflictRecord>(relativePath, ConflictPartition);

        return $"Conflict resolved: '{relativePath}' updated to use the {normalised} version.";
    }

    // -----------------------------------------------------------------------
    // Private — delta computation (pure, no I/O)
    // -----------------------------------------------------------------------

    private sealed record FileSyncDelta
    {
        public IReadOnlyList<string>       ToUpload   { get; init; } = [];
        public IReadOnlyList<string>       ToDownload { get; init; } = [];
        public IReadOnlyList<FileConflict> Conflicts  { get; init; } = [];
        public int                         Skipped    { get; init; }
    }

    private static FileSyncDelta ComputeDelta( IReadOnlyList<FileEntry> localFiles
                                             , IReadOnlyList<FileEntry> remoteFiles
                                             , IReadOnlyList<FileEntry> snapshot )
    {
        var toUpload   = new List<string>();
        var toDownload = new List<string>();
        var conflicts  = new List<FileConflict>();
        var skipped    = 0;

        var localByPath  = localFiles.ToDictionary(file => file.RelativePath, StringComparer.OrdinalIgnoreCase);
        var remoteByPath = remoteFiles.ToDictionary(file => file.RelativePath, StringComparer.OrdinalIgnoreCase);
        var snapByPath   = snapshot.ToDictionary(file => file.RelativePath, StringComparer.OrdinalIgnoreCase);

        var allPaths = localByPath.Keys.Union(remoteByPath.Keys, StringComparer.OrdinalIgnoreCase);

        foreach (var relativePath in allPaths)
        {
            localByPath.TryGetValue(relativePath, out var localEntry);
            remoteByPath.TryGetValue(relativePath, out var remoteEntry);
            snapByPath.TryGetValue(relativePath, out var snapshotEntry);

            if (localEntry is not null && remoteEntry is not null)
            {
                if (snapshotEntry is not null)
                {
                    var localChanged  = HasChangedSinceSnapshot(localEntry, snapshotEntry);
                    var remoteChanged = HasChangedSinceSnapshot(remoteEntry, snapshotEntry);

                    if (localChanged && remoteChanged)
                        conflicts.Add(new FileConflict
                                      {
                                          RelativePath        = relativePath
                                        , SourceModified      = localEntry.LastModified
                                        , DestinationModified = remoteEntry.LastModified
                                      });
                    else if (localChanged)
                        toUpload.Add(relativePath);
                    else if (remoteChanged)
                        toDownload.Add(relativePath);
                    else
                        skipped++;
                }
                else
                {
                    // No prior sync baseline — use last-modified comparison
                    if (localEntry.LastModified > remoteEntry.LastModified)
                        toUpload.Add(relativePath);
                    else if (remoteEntry.LastModified > localEntry.LastModified)
                        toDownload.Add(relativePath);
                    else
                        skipped++;
                }
            }
            else if (localEntry is not null)
            {
                toUpload.Add(relativePath);
            }
            else
            {
                toDownload.Add(relativePath);
            }
        }

        return new FileSyncDelta
               {
                   ToUpload   = toUpload
                 , ToDownload = toDownload
                 , Conflicts  = conflicts
                 , Skipped    = skipped
               };
    }

    private static bool HasChangedSinceSnapshot(FileEntry current, FileEntry snapshot)
        => current.LastModified != snapshot.LastModified
        || current.SizeBytes    != snapshot.SizeBytes;

    // -----------------------------------------------------------------------
    // Private — I/O helpers
    // -----------------------------------------------------------------------

    private async Task UploadFileAsync( string localBase
                                      , string relativePath
                                      , string remoteBase
                                      , CancellationToken ct )
    {
        var localFullPath = Path.Combine(localBase, relativePath);
        var remotePath    = BuildRemotePath(remoteBase, relativePath);

        await using var stream = await _localFileSystem.OpenReadAsync(localFullPath, ct);
        await _phoneProvider.UploadFileAsync(remotePath, stream, ct);
    }

    private async Task DownloadFileAsync( string remoteBase
                                        , string relativePath
                                        , string localBase
                                        , CancellationToken ct )
    {
        var remotePath    = BuildRemotePath(remoteBase, relativePath);
        var localFullPath = Path.Combine(localBase, relativePath);

        await using var stream = await _phoneProvider.DownloadFileAsync(remotePath, ct);
        await _localFileSystem.WriteAsync(localFullPath, stream, ct);

        // ENH-25: fire-and-forget document indexing for newly synced files
        if (_documentIndexingService is not null)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _documentIndexingService.IndexAsync(localFullPath);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Document indexing failed for synced file '{Path}'", localFullPath);
                }
            }, CancellationToken.None);
        }
    }

    private async Task StoreConflictRecordAsync( string relativePath
                                               , string localBase
                                               , string remoteBase
                                               , FileEntry localEntry
                                               , FileEntry remoteEntry
                                               , CancellationToken ct )
    {
        var record = new SyncConflictRecord
                     {
                         Id             = relativePath
                       , RelativePath   = relativePath
                       , LocalBasePath  = localBase
                       , RemoteBasePath = remoteBase
                       , LocalEntry     = localEntry
                       , RemoteEntry    = remoteEntry
                       , RecordedAt     = DateTimeOffset.UtcNow
                     };
        await _objectStore.Save(record, ConflictPartition, relativePath);
    }

    private async Task<string> ComputeMd5Async(string filePath, CancellationToken ct)
    {
        await using var stream = await _localFileSystem.OpenReadAsync(filePath, ct);
        using var md5  = MD5.Create();
        var hash       = await md5.ComputeHashAsync(stream, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    // -----------------------------------------------------------------------
    // Private — helpers
    // -----------------------------------------------------------------------

    private static string ComputeManifestId(string localPath, string remotePath)
        => $"{localPath.ToLowerInvariant()}::{remotePath.ToLowerInvariant()}";

    private static string BuildRemotePath(string remoteBase, string relativePath)
        => remoteBase.TrimEnd('/') + "/" + relativePath.Replace('\\', '/');

    private static string BuildSyncSummary(int filesCopied, int filesSkipped, int conflictCount)
    {
        var parts = new List<string>();
        if (filesCopied   > 0) parts.Add($"{filesCopied} file{(filesCopied == 1 ? "" : "s")} synced");
        if (filesSkipped  > 0) parts.Add($"{filesSkipped} unchanged");
        if (conflictCount > 0) parts.Add($"{conflictCount} conflict{(conflictCount == 1 ? "" : "s")} detected");

        return parts.Count > 0
                   ? string.Join(", ", parts) + "."
                   : "Nothing to sync — all files are up to date.";
    }

    private static string BuildPreviewSummary(int wouldCopy, int unchanged, int conflictCount)
    {
        var parts = new List<string>();
        if (wouldCopy     > 0) parts.Add($"{wouldCopy} file{(wouldCopy == 1 ? "" : "s")} would be synced");
        if (unchanged     > 0) parts.Add($"{unchanged} unchanged");
        if (conflictCount > 0) parts.Add($"{conflictCount} conflict{(conflictCount == 1 ? "" : "s")} would need resolution");

        return parts.Count > 0
                   ? string.Join(", ", parts) + "."
                   : "Everything is in sync — no changes detected.";
    }
}
