using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Domains.Media;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace CognitivePlatform.Tests;

public class MediaAttachmentServiceTests
{
    private readonly Mock<IObjectStore>                    _storeMock       = new();
    private readonly Mock<IMediaFileStorage>               _fileStorageMock = new();
    private readonly Mock<ILogger<MediaAttachmentService>> _loggerMock      = new();
    private readonly MediaAttachmentService                _service;

    public MediaAttachmentServiceTests()
    {
        var settings = new Mock<IOptions<MediaAttachmentSettings>>();
        settings.Setup(opt => opt.Value).Returns(new MediaAttachmentSettings
                                                  {
                                                      MediaRootPath = @"C:\TestMedia"
                                                  });
        _service = new MediaAttachmentService(_storeMock.Object
                                            , _fileStorageMock.Object
                                            , settings.Object
                                            , _loggerMock.Object);
    }

    // -----------------------------------------------------------------------
    // AddAttachmentAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task AddAttachmentAsync_CreatesRecordWithCorrectFields()
    {
        const string ownerType = "JournalEntry";
        const string ownerId   = "abc123";

        _fileStorageMock.Setup(fs => fs.EnsureDirectory(It.IsAny<string>()));
        _fileStorageMock.Setup(fs => fs.WriteAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
                        .Returns(Task.CompletedTask);
        _storeMock.Setup(store => store.Save(It.IsAny<MediaAttachment>(), It.IsAny<string>(), It.IsAny<string>()))
                  .ReturnsAsync("saved-id");

        await using var stream = new MemoryStream([1, 2, 3]);

        var result = await _service.AddAttachmentAsync(ownerType
                                                     , ownerId
                                                     , "photo.jpg"
                                                     , "image/jpeg"
                                                     , stream
                                                     , 3L);

        Assert.Equal(ownerType,    result.OwnerType);
        Assert.Equal(ownerId,      result.OwnerId);
        Assert.Equal("photo.jpg",  result.FileName);
        Assert.Equal("image/jpeg", result.ContentType);
        Assert.Equal(3L,           result.FileSizeBytes);
        Assert.NotEmpty(result.Id);
    }

    [Fact]
    public async Task AddAttachmentAsync_CallsEnsureDirectoryBeforeWrite()
    {
        var callOrder = new List<string>();

        _fileStorageMock.Setup(fs => fs.EnsureDirectory(It.IsAny<string>()))
                        .Callback((string path) => callOrder.Add("EnsureDirectory"));
        _fileStorageMock.Setup(fs => fs.WriteAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
                        .Callback((string path, Stream s, CancellationToken ct) => callOrder.Add("Write"))
                        .Returns(Task.CompletedTask);
        _storeMock.Setup(store => store.Save(It.IsAny<MediaAttachment>(), It.IsAny<string>(), It.IsAny<string>()))
                  .ReturnsAsync("id");

        await using var stream = new MemoryStream();
        await _service.AddAttachmentAsync("Task", "t1", "doc.pdf", "application/pdf", stream, 0);

        Assert.Equal("EnsureDirectory", callOrder[0]);
        Assert.Equal("Write",           callOrder[1]);
    }

    [Fact]
    public async Task AddAttachmentAsync_SanitizesIllegalFileNameChars()
    {
        string? savedPath = null;
        _fileStorageMock.Setup(fs => fs.EnsureDirectory(It.IsAny<string>()));
        _fileStorageMock.Setup(fs => fs.WriteAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
                        .Callback((string path, Stream s, CancellationToken ct) => savedPath = path)
                        .Returns(Task.CompletedTask);
        _storeMock.Setup(store => store.Save(It.IsAny<MediaAttachment>(), It.IsAny<string>(), It.IsAny<string>()))
                  .ReturnsAsync("id");

        await using var stream = new MemoryStream();
        await _service.AddAttachmentAsync("JournalEntry", "e1", "bad:name?.jpg", "image/jpeg", stream, 0);

        Assert.NotNull(savedPath);
        var fileName = Path.GetFileName(savedPath!);
        Assert.DoesNotContain(":", fileName);
        Assert.DoesNotContain("?", fileName);
    }

    [Fact]
    public async Task AddAttachmentAsync_BuildsStoragePathFromMediaRoot()
    {
        string? savedPath = null;
        _fileStorageMock.Setup(fs => fs.EnsureDirectory(It.IsAny<string>()));
        _fileStorageMock.Setup(fs => fs.WriteAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
                        .Callback((string path, Stream s, CancellationToken ct) => savedPath = path)
                        .Returns(Task.CompletedTask);
        _storeMock.Setup(store => store.Save(It.IsAny<MediaAttachment>(), It.IsAny<string>(), It.IsAny<string>()))
                  .ReturnsAsync("id");

        await using var stream = new MemoryStream();
        await _service.AddAttachmentAsync("JournalEntry", "entry1", "file.jpg", "image/jpeg", stream, 0);

        Assert.NotNull(savedPath);
        Assert.StartsWith(@"C:\TestMedia", savedPath!);
        Assert.Contains("JournalEntry", savedPath!);
        Assert.Contains("entry1",       savedPath!);
    }

    [Fact]
    public async Task AddAttachmentAsync_SavesRecordWithPartitionKey()
    {
        string? savedPartitionKey = null;
        _fileStorageMock.Setup(fs => fs.EnsureDirectory(It.IsAny<string>()));
        _fileStorageMock.Setup(fs => fs.WriteAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
                        .Returns(Task.CompletedTask);
        _storeMock.Setup(store => store.Save(It.IsAny<MediaAttachment>(), It.IsAny<string>(), It.IsAny<string>()))
                  .Callback((MediaAttachment attachment, string? partitionKey, string? id) => savedPartitionKey = partitionKey)
                  .ReturnsAsync("id");

        await using var stream = new MemoryStream();
        await _service.AddAttachmentAsync("Task", "t1", "a.txt", "text/plain", stream, 0);

        Assert.Equal("Task/t1", savedPartitionKey);
    }

    // -----------------------------------------------------------------------
    // GetAttachmentsAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetAttachmentsAsync_ReturnsListFromStore()
    {
        var expected = new List<MediaAttachment>
                       {
                           new() { Id = "a1", OwnerType = "JournalEntry", OwnerId = "e1", FileName = "img.jpg" , ContentType = "image/jpeg", StoragePath = "/tmp/img.jpg"  }
                         , new() { Id = "a2", OwnerType = "JournalEntry", OwnerId = "e1", FileName = "doc.pdf" , ContentType = "application/pdf", StoragePath = "/tmp/doc.pdf" }
                       };
        _storeMock.Setup(store => store.List<MediaAttachment>("JournalEntry/e1", null, null))
                  .Returns(expected);

        var result = await _service.GetAttachmentsAsync("JournalEntry", "e1");

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task GetAttachmentsAsync_ReturnsEmpty_WhenNoneExist()
    {
        _storeMock.Setup(store => store.List<MediaAttachment>("JournalEntry/e99", null, null))
                  .Returns(new List<MediaAttachment>());

        var result = await _service.GetAttachmentsAsync("JournalEntry", "e99");

        Assert.Empty(result);
    }

    // -----------------------------------------------------------------------
    // GetAttachmentAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetAttachmentAsync_ReturnsRecord_WhenFound()
    {
        var attachment = new MediaAttachment { Id = "id1", FileName = "f.jpg", OwnerType = "JournalEntry", OwnerId = "e1", ContentType = "image/jpeg", StoragePath = "/tmp/f.jpg" };
        _storeMock.Setup(store => store.Get<MediaAttachment>("id1", null))
                  .Returns(attachment);

        var result = await _service.GetAttachmentAsync("id1");

        Assert.NotNull(result);
        Assert.Equal("id1", result!.Id);
    }

    [Fact]
    public async Task GetAttachmentAsync_ReturnsNull_WhenNotFound()
    {
        _storeMock.Setup(store => store.Get<MediaAttachment>("missing", null))
                  .Returns((MediaAttachment?)null);

        var result = await _service.GetAttachmentAsync("missing");

        Assert.Null(result);
    }

    // -----------------------------------------------------------------------
    // GetAttachmentStreamAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetAttachmentStreamAsync_ReturnsStream_WhenFileExists()
    {
        var attachment = new MediaAttachment
                         {
                             Id          = "id1"
                           , FileName    = "f.jpg"
                           , OwnerType   = "JournalEntry"
                           , OwnerId     = "e1"
                           , ContentType = "image/jpeg"
                           , StoragePath = @"C:\TestMedia\JournalEntry\e1\f.jpg"
                         };
        var fileStream = new MemoryStream([10, 20, 30]);

        _storeMock.Setup(store => store.Get<MediaAttachment>("id1", null)).Returns(attachment);
        _fileStorageMock.Setup(fs => fs.Exists(@"C:\TestMedia\JournalEntry\e1\f.jpg")).Returns(true);
        _fileStorageMock.Setup(fs => fs.OpenRead(@"C:\TestMedia\JournalEntry\e1\f.jpg")).Returns(fileStream);

        var result = await _service.GetAttachmentStreamAsync("id1");

        Assert.NotNull(result);
    }

    [Fact]
    public async Task GetAttachmentStreamAsync_ReturnsNull_WhenAttachmentNotFound()
    {
        _storeMock.Setup(store => store.Get<MediaAttachment>("missing", null))
                  .Returns((MediaAttachment?)null);

        var result = await _service.GetAttachmentStreamAsync("missing");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAttachmentStreamAsync_ReturnsNull_WhenFileIsMissing()
    {
        var attachment = new MediaAttachment
                         {
                             Id          = "id1"
                           , FileName    = "f.jpg"
                           , OwnerType   = "JournalEntry"
                           , OwnerId     = "e1"
                           , ContentType = "image/jpeg"
                           , StoragePath = @"C:\TestMedia\gone.jpg"
                         };
        _storeMock.Setup(store => store.Get<MediaAttachment>("id1", null)).Returns(attachment);
        _fileStorageMock.Setup(fs => fs.Exists(@"C:\TestMedia\gone.jpg")).Returns(false);

        var result = await _service.GetAttachmentStreamAsync("id1");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAttachmentStreamAsync_ReturnsNull_WhenAttachmentIsDeleted()
    {
        var attachment = new MediaAttachment
                         {
                             Id          = "id1"
                           , FileName    = "f.jpg"
                           , OwnerType   = "JournalEntry"
                           , OwnerId     = "e1"
                           , ContentType = "image/jpeg"
                           , StoragePath = @"C:\TestMedia\f.jpg"
                           , IsDeleted   = true
                         };
        _storeMock.Setup(store => store.Get<MediaAttachment>("id1", null)).Returns(attachment);

        var result = await _service.GetAttachmentStreamAsync("id1");

        Assert.Null(result);
    }

    // -----------------------------------------------------------------------
    // DeleteAttachmentAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task DeleteAttachmentAsync_ReturnsTrue_WhenSoftDeleteSucceeds()
    {
        _storeMock.Setup(store => store.SoftDelete<MediaAttachment>("id1", null)).Returns(true);

        var result = await _service.DeleteAttachmentAsync("id1");

        Assert.True(result);
    }

    [Fact]
    public async Task DeleteAttachmentAsync_ReturnsFalse_WhenRecordNotFound()
    {
        _storeMock.Setup(store => store.SoftDelete<MediaAttachment>("missing", null)).Returns(false);

        var result = await _service.DeleteAttachmentAsync("missing");

        Assert.False(result);
    }

    [Fact]
    public async Task DeleteAttachmentAsync_DoesNotDeleteFile()
    {
        _storeMock.Setup(store => store.SoftDelete<MediaAttachment>("id1", null)).Returns(true);

        await _service.DeleteAttachmentAsync("id1");

        _fileStorageMock.Verify(fs => fs.Delete(It.IsAny<string>()), Times.Never);
    }

    // -----------------------------------------------------------------------
    // GetAttachmentCountAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetAttachmentCountAsync_ReturnsCountFromStore()
    {
        _storeMock.Setup(store => store.List<MediaAttachment>("JournalEntry/e1", null, null))
                  .Returns(new List<MediaAttachment>
                           {
                               new() { Id = "a1", OwnerType = "JournalEntry", OwnerId = "e1", FileName = "x.jpg", ContentType = "image/jpeg", StoragePath = "/tmp/x.jpg" }
                             , new() { Id = "a2", OwnerType = "JournalEntry", OwnerId = "e1", FileName = "y.jpg", ContentType = "image/jpeg", StoragePath = "/tmp/y.jpg" }
                           });

        var count = await _service.GetAttachmentCountAsync("JournalEntry", "e1");

        Assert.Equal(2, count);
    }

    [Fact]
    public async Task GetAttachmentCountAsync_ReturnsZero_WhenNoAttachments()
    {
        _storeMock.Setup(store => store.List<MediaAttachment>("JournalEntry/none", null, null))
                  .Returns(new List<MediaAttachment>());

        var count = await _service.GetAttachmentCountAsync("JournalEntry", "none");

        Assert.Equal(0, count);
    }
}
