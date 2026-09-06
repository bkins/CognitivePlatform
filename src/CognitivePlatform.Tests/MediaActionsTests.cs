using CognitivePlatform.Api.Domains.Media;
using CognitivePlatform.Api.Domains.Journal.Interfaces;
using CognitivePlatform.Api.Domains.Journal;
using CognitivePlatform.Api.Domains.Tasks;
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

    [Fact]
    public async Task ListAttachments_UsesPositionNumber_WhenOwnerIsResolved()
    {
        var journalMock = new Mock<IJournalService>();
        var taskMock    = new Mock<ITaskService>();

        var entryId = "e1-guid";
        var entry = new JournalEntry { Id = entryId };
        var revision = new JournalRevision();
        var entryWithRevision = new CognitivePlatform.Api.Models.JournalEntryWithRevision(entry, revision, false);
        var entries = new List<(int Position, CognitivePlatform.Api.Models.JournalEntryWithRevision EntryWithRevision)>
                      {
                          (3, entryWithRevision)
                      };
        journalMock.Setup(j => j.GetOrderedEntries()).Returns(entries);

        var actionsWithServices = new MediaActions(_serviceMock.Object, journalMock.Object, taskMock.Object);

        var attachments = new List<MediaAttachment>
                          {
                              new()
                              {
                                  Id          = "a1"
                                , OwnerType   = "JournalEntry"
                                , OwnerId     = entryId
                                , FileName    = "photo.jpg"
                                , ContentType = "image/jpeg"
                              }
                          };
        _serviceMock.Setup(service => service.GetAttachmentsAsync("JournalEntry", entryId))
                    .ReturnsAsync(attachments);

        var result = await actionsWithServices.ListAttachments("JournalEntry", entryId);

        Assert.Contains("journal entry #3", result);
        Assert.DoesNotContain(entryId, result);
    }

    [Fact]
    public async Task ListAttachments_HidesOwnerId_WhenOwnerCannotBeResolved()
    {
        const string ownerId = "7b5e4d53-2dc8-4c32-a5df-6d8fea6f8b9e";
        _serviceMock.Setup(service => service.GetAttachmentsAsync("JournalEntry", ownerId))
                    .ReturnsAsync(new List<MediaAttachment>());

        var result = await _actions.ListAttachments("JournalEntry", ownerId);

        Assert.Contains("requested JournalEntry item", result);
        Assert.DoesNotContain(ownerId, result);
    }
}
