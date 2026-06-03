using CognitivePlatform.Api.Domains.Media;
using Moq;

namespace CognitivePlatform.Tests;

public class MediaActionsTests
{
    private readonly Mock<IMediaAttachmentService> _serviceMock = new();
    private readonly MediaActions                   _actions;

    public MediaActionsTests()
    {
        _actions = new MediaActions(_serviceMock.Object);
    }

    // -----------------------------------------------------------------------
    // ListAttachments
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ListAttachments_ReturnsNoAttachmentsMessage_WhenNoneExist()
    {
        _serviceMock.Setup(service => service.GetAttachmentsAsync("JournalEntry", "e1"))
                    .ReturnsAsync(new List<MediaAttachment>());

        var result = await _actions.ListAttachments("JournalEntry", "e1");

        Assert.Contains("No attachments", result);
    }

    [Fact]
    public async Task ListAttachments_ListsEachFileName()
    {
        var attachments = new List<MediaAttachment>
                          {
                              new()
                              {
                                  Id            = "a1"
                                , OwnerType     = "JournalEntry"
                                , OwnerId       = "e1"
                                , FileName      = "photo.jpg"
                                , ContentType   = "image/jpeg"
                                , FileSizeBytes = 2048
                                , StoragePath   = "/tmp/photo.jpg"
                              }
                            , new()
                              {
                                  Id            = "a2"
                                , OwnerType     = "JournalEntry"
                                , OwnerId       = "e1"
                                , FileName      = "notes.pdf"
                                , ContentType   = "application/pdf"
                                , FileSizeBytes = 4096
                                , StoragePath   = "/tmp/notes.pdf"
                              }
                          };
        _serviceMock.Setup(service => service.GetAttachmentsAsync("JournalEntry", "e1"))
                    .ReturnsAsync(attachments);

        var result = await _actions.ListAttachments("JournalEntry", "e1");

        Assert.Contains("photo.jpg", result);
        Assert.Contains("notes.pdf", result);
    }

    [Fact]
    public async Task ListAttachments_IncludesAttachmentCount()
    {
        var attachments = new List<MediaAttachment>
                          {
                              new()
                              {
                                  Id          = "a1"
                                , OwnerType   = "Task"
                                , OwnerId     = "t1"
                                , FileName    = "file.txt"
                                , ContentType = "text/plain"
                                , StoragePath = "/tmp/file.txt"
                              }
                          };
        _serviceMock.Setup(service => service.GetAttachmentsAsync("Task", "t1"))
                    .ReturnsAsync(attachments);

        var result = await _actions.ListAttachments("Task", "t1");

        Assert.Contains("1", result);
    }

    [Fact]
    public async Task ListAttachments_CallsGetAttachmentsAsync()
    {
        _serviceMock.Setup(service => service.GetAttachmentsAsync("JournalEntry", "e1"))
                    .ReturnsAsync(new List<MediaAttachment>());

        await _actions.ListAttachments("JournalEntry", "e1");

        _serviceMock.Verify(service => service.GetAttachmentsAsync("JournalEntry", "e1"), Times.Once);
    }

    // -----------------------------------------------------------------------
    // GetAttachmentCount
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetAttachmentCount_ReturnsZeroMessage_WhenNoneExist()
    {
        _serviceMock.Setup(service => service.GetAttachmentCountAsync("JournalEntry", "e1"))
                    .ReturnsAsync(0);

        var result = await _actions.GetAttachmentCount("JournalEntry", "e1");

        Assert.Contains("No attachments", result);
    }

    [Fact]
    public async Task GetAttachmentCount_ReturnsSingularMessage_WhenOneExists()
    {
        _serviceMock.Setup(service => service.GetAttachmentCountAsync("JournalEntry", "e1"))
                    .ReturnsAsync(1);

        var result = await _actions.GetAttachmentCount("JournalEntry", "e1");

        Assert.Contains("1 attachment", result);
        Assert.DoesNotContain("attachments", result);
    }

    [Fact]
    public async Task GetAttachmentCount_ReturnsPluralMessage_WhenManyExist()
    {
        _serviceMock.Setup(service => service.GetAttachmentCountAsync("Task", "t1"))
                    .ReturnsAsync(5);

        var result = await _actions.GetAttachmentCount("Task", "t1");

        Assert.Contains("5 attachments", result);
    }

    [Fact]
    public async Task GetAttachmentCount_CallsGetAttachmentCountAsync()
    {
        _serviceMock.Setup(service => service.GetAttachmentCountAsync("JournalEntry", "e1"))
                    .ReturnsAsync(0);

        await _actions.GetAttachmentCount("JournalEntry", "e1");

        _serviceMock.Verify(service => service.GetAttachmentCountAsync("JournalEntry", "e1"), Times.Once);
    }
}
