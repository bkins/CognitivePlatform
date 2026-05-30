using Moq;
using CognitivePlatform.Api.Domains.FileSync;
using CognitivePlatform.Api.Integrations.FileSync;
using CognitivePlatform.Api.Integrations.FileSync.Models;

namespace CognitivePlatform.Tests;

public class FileSyncActionsTests
{
    private readonly Mock<IFileSyncProvider> _providerMock = new();
    private readonly FileSyncActions         _actions;

    public FileSyncActionsTests()
    {
        _actions = new FileSyncActions(_providerMock.Object);
    }

    // ================================================================
    // ListFiles
    // ================================================================

    [Fact]
    public async Task ListFiles_ReturnsNotConnected_WhenProviderNotConnected()
    {
        _providerMock.SetupGet(provider => provider.IsConnected).Returns(false);

        var result = await _actions.ListFiles("/storage/Documents");

        Assert.Contains("not connected", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ListFiles_ReturnsFileList_WhenProviderConnected()
    {
        _providerMock.SetupGet(provider => provider.IsConnected).Returns(true);
        _providerMock.SetupGet(provider => provider.DeviceName).Returns("Pixel 8");
        _providerMock.Setup(provider => provider.ListFilesAsync( It.IsAny<string>()
                                                               , It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new FileEntry[]
                                   {
                                       new() { RelativePath = "notes.txt"
                                             , SizeBytes    = 2048
                                             , LastModified = DateTimeOffset.UtcNow.AddDays(-1) }
                                   });

        var result = await _actions.ListFiles("/storage/Documents");

        Assert.Contains("notes.txt", result);
        Assert.Contains("Pixel 8",   result);
    }

    [Fact]
    public async Task ListFiles_ReturnsEmptyMessage_WhenNoFilesFound()
    {
        _providerMock.SetupGet(provider => provider.IsConnected).Returns(true);
        _providerMock.SetupGet(provider => provider.DeviceName).Returns("Pixel 8");
        _providerMock.Setup(provider => provider.ListFilesAsync( It.IsAny<string>()
                                                               , It.IsAny<CancellationToken>()))
                     .ReturnsAsync(Array.Empty<FileEntry>());

        var result = await _actions.ListFiles("/storage/Empty");

        Assert.Contains("No files found", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ListFiles_ReturnsUnavailable_WhenProviderThrows()
    {
        _providerMock.SetupGet(provider => provider.IsConnected).Returns(true);
        _providerMock.Setup(provider => provider.ListFilesAsync( It.IsAny<string>()
                                                               , It.IsAny<CancellationToken>()))
                     .ThrowsAsync(new HttpRequestException("connection refused"));

        var result = await _actions.ListFiles("/storage/Documents");

        Assert.Contains("unavailable", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ListFiles_ShowsItemCount_WhenMultipleFilesReturned()
    {
        _providerMock.SetupGet(provider => provider.IsConnected).Returns(true);
        _providerMock.SetupGet(provider => provider.DeviceName).Returns("Pixel 8");
        _providerMock.Setup(provider => provider.ListFilesAsync( It.IsAny<string>()
                                                               , It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new FileEntry[]
                                   {
                                       new() { RelativePath = "a.txt", SizeBytes = 512,  LastModified = DateTimeOffset.UtcNow }
                                     , new() { RelativePath = "b.txt", SizeBytes = 1024, LastModified = DateTimeOffset.UtcNow }
                                     , new() { RelativePath = "c.txt", SizeBytes = 2048, LastModified = DateTimeOffset.UtcNow }
                                   });

        var result = await _actions.ListFiles("/storage/Notes");

        Assert.Contains("3 items", result);
        Assert.Contains("a.txt",   result);
        Assert.Contains("b.txt",   result);
        Assert.Contains("c.txt",   result);
    }

    // ================================================================
    // SyncFolder
    // ================================================================

    [Fact]
    public async Task SyncFolder_ReturnsNotConnected_WhenProviderNotConnected()
    {
        _providerMock.SetupGet(provider => provider.IsConnected).Returns(false);

        var result = await _actions.SyncFolder(@"C:\Documents\Notes", "phone");

        Assert.Contains("not connected", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SyncFolder_ReturnsNonEmptyMessage_WhenProviderConnected()
    {
        _providerMock.SetupGet(provider => provider.IsConnected).Returns(true);

        var result = await _actions.SyncFolder(@"C:\Documents\Notes", "phone");

        Assert.NotEmpty(result);
    }

    // ================================================================
    // GetSyncStatus
    // ================================================================

    [Fact]
    public async Task GetSyncStatus_ReturnsNotConnected_WhenProviderNotConnected()
    {
        _providerMock.SetupGet(provider => provider.IsConnected).Returns(false);

        var result = await _actions.GetSyncStatus();

        Assert.Contains("not connected", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetSyncStatus_IncludesDeviceName_WhenProviderConnected()
    {
        _providerMock.SetupGet(provider => provider.IsConnected).Returns(true);
        _providerMock.SetupGet(provider => provider.DeviceName).Returns("Pixel 8");

        var result = await _actions.GetSyncStatus();

        Assert.Contains("Pixel 8", result);
    }

    // ================================================================
    // ResolveSyncConflict
    // ================================================================

    [Fact]
    public async Task ResolveSyncConflict_ReturnsNotConnected_WhenProviderNotConnected()
    {
        _providerMock.SetupGet(provider => provider.IsConnected).Returns(false);

        var result = await _actions.ResolveSyncConflict("README.md", "laptop");

        Assert.Contains("not connected", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ResolveSyncConflict_IncludesConflictId_WhenProviderConnected()
    {
        _providerMock.SetupGet(provider => provider.IsConnected).Returns(true);

        var result = await _actions.ResolveSyncConflict("README.md", "laptop");

        Assert.Contains("README.md", result);
    }

    [Fact]
    public async Task ResolveSyncConflict_IncludesResolution_WhenProviderConnected()
    {
        _providerMock.SetupGet(provider => provider.IsConnected).Returns(true);

        var result = await _actions.ResolveSyncConflict("notes.txt", "phone");

        Assert.Contains("phone", result, StringComparison.OrdinalIgnoreCase);
    }
}
