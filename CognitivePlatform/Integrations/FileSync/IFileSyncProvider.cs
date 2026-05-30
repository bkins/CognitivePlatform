using CognitivePlatform.Api.Integrations.FileSync.Models;

namespace CognitivePlatform.Api.Integrations.FileSync;

/// <summary>
/// Abstracts remote file access so the rest of the platform does not depend on
/// the phone gateway directly. <see cref="DisconnectedFileSyncProvider"/> is the DI
/// default until a phone is configured (Phase F.1-B).
/// </summary>
public interface IFileSyncProvider
{
    /// <summary>True when the remote device is reachable and responding.</summary>
    bool   IsConnected { get; }

    /// <summary>Human-readable name of the connected device, e.g. "Pixel 8".</summary>
    string DeviceName  { get; }

    Task<IReadOnlyList<FileEntry>> ListFilesAsync   (string remotePath,                 CancellationToken ct = default);
    Task<Stream>                   DownloadFileAsync (string remotePath,                 CancellationToken ct = default);
    Task                           UploadFileAsync   (string remotePath, Stream content, CancellationToken ct = default);
    Task                           DeleteFileAsync   (string remotePath,                 CancellationToken ct = default);
    Task<bool>                     PingAsync         (                                   CancellationToken ct = default);
}
