using CognitivePlatform.Api.Integrations.FileSync.Models;

namespace CognitivePlatform.Api.Domains.FileSync;

public interface IFileSyncService
{
    Task<SyncResult>   SyncFolderAsync         (string localPath, string remotePath, CancellationToken ct = default);
    Task<SyncResult>   PreviewSyncAsync        (string localPath, string remotePath, CancellationToken ct = default);
    Task<SyncManifest> GetManifestAsync        (string path,                         CancellationToken ct = default);
    Task<string>       ResolveSyncConflictAsync(string relativePath, string resolution, CancellationToken ct = default);
}
