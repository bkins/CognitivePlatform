using CognitivePlatform.Api.Integrations.FileSync.Models;

namespace CognitivePlatform.Api.Integrations.FileSync;

/// <summary>
/// Default IFileSyncProvider registered in DI before a phone endpoint is configured.
/// IsConnected always returns false; all data methods throw to prevent accidental use.
/// Replaced by HttpFileSyncProvider in Phase F.1-B.
/// </summary>
public sealed class DisconnectedFileSyncProvider : IFileSyncProvider
{
    public bool   IsConnected => false;
    public string DeviceName  => string.Empty;

    public Task<IReadOnlyList<FileEntry>> ListFilesAsync   (string remotePath,                 CancellationToken ct = default)
        => throw new InvalidOperationException("File sync provider is not connected.");

    public Task<Stream>                   DownloadFileAsync (string remotePath,                 CancellationToken ct = default)
        => throw new InvalidOperationException("File sync provider is not connected.");

    public Task                           UploadFileAsync   (string remotePath, Stream content, CancellationToken ct = default)
        => throw new InvalidOperationException("File sync provider is not connected.");

    public Task                           DeleteFileAsync   (string remotePath,                 CancellationToken ct = default)
        => throw new InvalidOperationException("File sync provider is not connected.");

    public Task<bool>                     PingAsync         (                                   CancellationToken ct = default)
        => Task.FromResult(false);
}
