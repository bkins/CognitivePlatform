using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Domains.FileSync;
using CognitivePlatform.Api.Integrations.FileSync;
using CognitivePlatform.Api.Integrations.FileSync.Models;

namespace CognitivePlatform.Tests;

public class FileSyncServiceTests
{
    private readonly Mock<IFileSyncProvider> _providerMock  = new();
    private readonly Mock<ILocalFileSystem>  _localFsMock   = new();
    private readonly Mock<IObjectStore>      _storeMock     = new();
    private readonly FileSyncService         _service;

    public FileSyncServiceTests()
    {
        _service = new FileSyncService( _providerMock.Object
                                      , _localFsMock.Object
                                      , _storeMock.Object
                                      , NullLogger<FileSyncService>.Instance );
    }

    // ================================================================
    // PreviewSyncAsync
    // ================================================================

    [Fact]
    public async Task PreviewSyncAsync_ReturnsCorrectCounts_ForMixedDelta()
    {
        var baseTime = DateTimeOffset.UtcNow.AddHours(-1);

        var localFiles = new FileEntry[]
                         {
                             new() { RelativePath = "same.txt",    SizeBytes = 100, LastModified = baseTime }
                           , new() { RelativePath = "local.txt",   SizeBytes = 200, LastModified = baseTime.AddMinutes(30) }
                           , new() { RelativePath = "remote.txt",  SizeBytes = 300, LastModified = baseTime }
                         };

        var remoteFiles = new FileEntry[]
                          {
                              new() { RelativePath = "same.txt",   SizeBytes = 100, LastModified = baseTime }
                            , new() { RelativePath = "local.txt",  SizeBytes = 200, LastModified = baseTime }
                            , new() { RelativePath = "remote.txt", SizeBytes = 300, LastModified = baseTime.AddMinutes(30) }
                          };

        _localFsMock.Setup(fs => fs.ListAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(localFiles);
        _providerMock.Setup(provider => provider.ListFilesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                     .ReturnsAsync(remoteFiles);

        var result = await _service.PreviewSyncAsync(@"C:\local", "/remote");

        Assert.Equal(2, result.FilesCopied);
        Assert.Equal(1, result.FilesSkipped);
        Assert.Empty(result.Conflicts);
    }

    // ================================================================
    // SyncFolderAsync — download (remote is newer)
    // ================================================================

    [Fact]
    public async Task SyncFolderAsync_Downloads_WhenRemoteIsNewer()
    {
        var older = DateTimeOffset.UtcNow.AddHours(-2);
        var newer = DateTimeOffset.UtcNow.AddHours(-1);

        var localFiles  = new FileEntry[] { new() { RelativePath = "notes.txt", SizeBytes = 100, LastModified = older } };
        var remoteFiles = new FileEntry[] { new() { RelativePath = "notes.txt", SizeBytes = 120, LastModified = newer } };

        SetupListMocks(localFiles, remoteFiles);
        SetupSaveManifest();

        var downloadedContent = new MemoryStream(new byte[] { 1, 2, 3 });
        _providerMock.Setup(provider => provider.DownloadFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                     .ReturnsAsync(downloadedContent);
        _localFsMock.Setup(fs => fs.WriteAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
                    .Returns(Task.CompletedTask);

        var result = await _service.SyncFolderAsync(@"C:\local", "/remote");

        Assert.Equal(1, result.FilesCopied);
        Assert.Equal(0, result.FilesSkipped);
        Assert.Empty(result.Conflicts);
        _providerMock.Verify(provider => provider.DownloadFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        _localFsMock.Verify(fs => fs.WriteAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ================================================================
    // SyncFolderAsync — upload (local is newer)
    // ================================================================

    [Fact]
    public async Task SyncFolderAsync_Uploads_WhenLocalIsNewer()
    {
        var older = DateTimeOffset.UtcNow.AddHours(-2);
        var newer = DateTimeOffset.UtcNow.AddHours(-1);

        var localFiles  = new FileEntry[] { new() { RelativePath = "notes.txt", SizeBytes = 120, LastModified = newer } };
        var remoteFiles = new FileEntry[] { new() { RelativePath = "notes.txt", SizeBytes = 100, LastModified = older } };

        SetupListMocks(localFiles, remoteFiles);
        SetupSaveManifest();

        _localFsMock.Setup(fs => fs.OpenReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new MemoryStream(new byte[] { 9, 8, 7 }));
        _providerMock.Setup(provider => provider.UploadFileAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
                     .Returns(Task.CompletedTask);

        var result = await _service.SyncFolderAsync(@"C:\local", "/remote");

        Assert.Equal(1, result.FilesCopied);
        Assert.Equal(0, result.FilesSkipped);
        Assert.Empty(result.Conflicts);
        _localFsMock.Verify(fs => fs.OpenReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        _providerMock.Verify(provider => provider.UploadFileAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ================================================================
    // SyncFolderAsync — skip identical
    // ================================================================

    [Fact]
    public async Task SyncFolderAsync_Skips_WhenFilesAreIdentical()
    {
        var sameTime = DateTimeOffset.UtcNow.AddHours(-1);

        var localFiles  = new FileEntry[] { new() { RelativePath = "notes.txt", SizeBytes = 100, LastModified = sameTime } };
        var remoteFiles = new FileEntry[] { new() { RelativePath = "notes.txt", SizeBytes = 100, LastModified = sameTime } };

        SetupListMocks(localFiles, remoteFiles);
        SetupSaveManifest();

        var result = await _service.SyncFolderAsync(@"C:\local", "/remote");

        Assert.Equal(0, result.FilesCopied);
        Assert.Equal(1, result.FilesSkipped);
        Assert.Empty(result.Conflicts);
        _providerMock.Verify(provider => provider.DownloadFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _localFsMock.Verify(fs => fs.OpenReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ================================================================
    // SyncFolderAsync — conflict (both changed since last snapshot)
    // ================================================================

    [Fact]
    public async Task SyncFolderAsync_RecordsConflict_WhenBothSidesChangedSinceLastSync()
    {
        var snapshotTime = DateTimeOffset.UtcNow.AddHours(-3);
        var localModTime = DateTimeOffset.UtcNow.AddHours(-1);
        var remoteModTime = DateTimeOffset.UtcNow.AddMinutes(-30);

        var snapshotEntry = new FileEntry { RelativePath = "notes.txt", SizeBytes = 100, LastModified = snapshotTime };
        var localFiles    = new FileEntry[] { new() { RelativePath = "notes.txt", SizeBytes = 150, LastModified = localModTime  } };
        var remoteFiles   = new FileEntry[] { new() { RelativePath = "notes.txt", SizeBytes = 90,  LastModified = remoteModTime } };

        var storedManifest = new SyncManifest
                             {
                                 ManifestId     = "test-manifest"
                               , SnapshotAtSync = [snapshotEntry]
                             };

        SetupListMocks(localFiles, remoteFiles);

        _storeMock.Setup(store => store.Get<SyncManifest>(It.IsAny<string>(), It.IsAny<string>()))
                  .Returns(storedManifest);
        _storeMock.Setup(store => store.Save(It.IsAny<SyncConflictRecord>(), It.IsAny<string>(), It.IsAny<string>()))
                  .ReturnsAsync("conflict-id");
        SetupSaveManifest();

        var result = await _service.SyncFolderAsync(@"C:\local", "/remote");

        Assert.Equal(0,           result.FilesCopied);
        var conflict = Assert.Single(result.Conflicts);
        Assert.Equal("notes.txt", conflict.RelativePath);
        _storeMock.Verify(store => store.Save(It.IsAny<SyncConflictRecord>(), ConflictPartition, It.IsAny<string>()), Times.Once);
    }

    // ================================================================
    // Private helpers
    // ================================================================

    private const string ConflictPartition = "file_sync_conflict";

    private void SetupListMocks(IReadOnlyList<FileEntry> localFiles, IReadOnlyList<FileEntry> remoteFiles)
    {
        _localFsMock.Setup(fs => fs.ListAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(localFiles);
        _providerMock.Setup(provider => provider.ListFilesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                     .ReturnsAsync(remoteFiles);
    }

    private void SetupSaveManifest()
    {
        _storeMock.Setup(store => store.Save(It.IsAny<SyncManifest>(), It.IsAny<string>(), It.IsAny<string>()))
                  .ReturnsAsync("manifest-id");
    }
}
