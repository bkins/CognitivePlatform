using Moq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using CognitivePlatform.Api.Controllers;
using CognitivePlatform.Api.Domains.Journal;
using CognitivePlatform.Api.Domains.Journal.Interfaces;
using CognitivePlatform.Api.Domains.Media;
using CognitivePlatform.Api.Models;
using CognitivePlatform.Api.Models.TestingTemp;

namespace CognitivePlatform.Tests;

public class JournalControllerTests
{
    private readonly Mock<IJournalService>            _journalServiceMock            = new();
    private readonly Mock<IJournalRevisionRepository> _journalRevisionRepositoryMock = new();
    private readonly Mock<IMediaAttachmentService>    _mediaServiceMock              = new();
    private readonly JournalController                 _controller;

    public JournalControllerTests()
    {
        _controller = new JournalController(_journalServiceMock.Object
                                          , _journalRevisionRepositoryMock.Object
                                          , _mediaServiceMock.Object);
    }

    [Fact]
    public async Task GetById_ReturnsOkWithJournalEntryDto_WhenEntryExists()
    {
        var id       = Guid.NewGuid();
        var idString = id.ToString("N");

        var entry    = new JournalEntry { Id = idString, CreatedUtc = DateTimeOffset.UtcNow };
        var revision = new JournalRevision
                       {
                           RevisionId = Guid.NewGuid().ToString("N")
                         , EntryId    = idString
                         , Text       = "Sample journal text."
                         , Tags       = new List<string> { "tag1", "tag2" }
                         , Mood       = "Happy"
                         , MoodScore  = 4
                         , State      = JournalEntryState.Active
                       };

        var entryWithRevision = new JournalEntryWithRevision(entry, revision, false);

        _journalServiceMock.Setup(service => service.GetById(idString))
                           .Returns(entryWithRevision);

        _mediaServiceMock.Setup(service => service.GetAttachmentCountAsync("JournalEntry", idString))
                         .ReturnsAsync(2);

        var actionResult = await _controller.GetById(id, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var dto      = Assert.IsType<JournalEntryDto>(okResult.Value);

        Assert.Equal(id, dto.Id);
        Assert.Equal("Sample journal text.", dto.Text);
        Assert.Equal(new[] { "tag1", "tag2" }, dto.Tags);
        Assert.Equal("Happy", dto.Mood);
        Assert.Equal(4, dto.MoodScore);
        Assert.False(dto.IsEdited);
        Assert.Equal(2, dto.AttachmentCount);
    }

    [Fact]
    public void Get_ReturnsOkWithEntries_WhenCalled()
    {
        var entries = new List<JournalEntryWithRevision>();
        _journalServiceMock.Setup(service => service.ListEntries(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>()))
                           .Returns(entries);

        var actionResult = _controller.Get();

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        Assert.Same(entries, okResult.Value);
    }

    [Fact]
    public void GetRevisions_ReturnsNotFound_WhenJournalDoesNotExist()
    {
        var journalId = Guid.NewGuid();
        _journalServiceMock.Setup(service => service.Exists(journalId))
                           .Returns(false);

        var actionResult = _controller.GetRevisions(journalId);

        Assert.IsType<NotFoundResult>(actionResult.Result);
    }

    [Fact]
    public void GetRevisions_ReturnsOkWithRevisionDtos_WhenJournalExists()
    {
        var journalId    = Guid.NewGuid();
        var entryId      = journalId.ToString("N");
        var revisionGuid = Guid.NewGuid();

        var revisions = new List<JournalRevision>
                        {
                            new()
                            {
                                RevisionId = revisionGuid.ToString("N")
                              , EntryId    = entryId
                              , CreatedUtc = DateTimeOffset.UtcNow
                              , Text       = "Revision 1 text"
                              , Tags       = new List<string> { "work" }
                              , Mood       = "Focused"
                              , MoodScore  = 5
                            }
                        };

        _journalServiceMock.Setup(service => service.Exists(journalId))
                           .Returns(true);

        _journalRevisionRepositoryMock.Setup(repo => repo.GetRevisionsByEntryId(entryId))
                                      .Returns(revisions);

        var actionResult = _controller.GetRevisions(journalId);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var dtos     = Assert.IsAssignableFrom<IEnumerable<JournalRevisionDto>>(okResult.Value);
        var dtoList  = dtos.ToList();

        Assert.Single(dtoList);
        Assert.Equal(revisionGuid, dtoList[0].RevisionId);
        Assert.Equal("Revision 1 text", dtoList[0].Text);
    }

    [Fact]
    public void EditEntry_Test_ReturnsNoContent_WhenEditSucceeds()
    {
        var journalId = Guid.NewGuid();
        var request   = new JournalEditTestRequest
                        {
                            Text      = "Updated text"
                          , Tags      = new[] { "updated" }
                          , Mood      = "Calm"
                          , MoodScore = 3
                        };

        var revision = new JournalRevision
                       {
                           RevisionId = Guid.NewGuid().ToString("N")
                         , EntryId    = journalId.ToString("N")
                         , Text       = "Updated text"
                       };

        _journalServiceMock.Setup(service => service.EditEntry(journalId.ToString("N")
                                                             , "Updated text"
                                                             , request.Tags
                                                             , false
                                                             , "Calm"
                                                             , false
                                                             , 3
                                                             , false
                                                             , null
                                                             , null))
                           .Returns(revision);

        var actionResult = _controller.EditEntry_Test(journalId, request);

        Assert.IsType<NoContentResult>(actionResult);
    }

    [Fact]
    public void EditEntry_Test_ReturnsNotFound_WhenKeyNotFoundExceptionThrown()
    {
        var journalId = Guid.NewGuid();
        var request   = new JournalEditTestRequest { Text = "Updated text" };

        _journalServiceMock.Setup(service => service.EditEntry(journalId.ToString("N")
                                                             , "Updated text"
                                                             , null
                                                             , false
                                                             , null
                                                             , false
                                                             , null
                                                             , false
                                                             , null
                                                             , null))
                           .Throws<KeyNotFoundException>();

        var actionResult = _controller.EditEntry_Test(journalId, request);

        Assert.IsType<NotFoundResult>(actionResult);
    }

    [Fact]
    public async Task UploadMedia_ReturnsNotFound_WhenJournalDoesNotExist()
    {
        var journalId = Guid.NewGuid();
        var fileMock  = new Mock<IFormFile>();

        _journalServiceMock.Setup(service => service.Exists(journalId))
                           .Returns(false);

        var actionResult = await _controller.UploadMedia(journalId, fileMock.Object);

        Assert.IsType<NotFoundResult>(actionResult.Result);
    }

    [Fact]
    public async Task UploadMedia_ReturnsBadRequest_WhenFileIsNull()
    {
        var journalId = Guid.NewGuid();
        _journalServiceMock.Setup(service => service.Exists(journalId))
                           .Returns(true);

        var actionResult = await _controller.UploadMedia(journalId, null!);

        var badRequestResult = Assert.IsType<BadRequestObjectResult>(actionResult.Result);
        Assert.Equal("No file provided.", badRequestResult.Value);
    }

    [Fact]
    public async Task UploadMedia_ReturnsCreated_WhenValidFileUploaded()
    {
        var journalId    = Guid.NewGuid();
        var mediaGuid    = Guid.NewGuid();
        var mediaId      = mediaGuid.ToString("N");
        var fileMock     = new Mock<IFormFile>();
        var stream       = new MemoryStream(new byte[] { 1, 2, 3 });

        fileMock.Setup(file => file.Length).Returns(3);
        fileMock.Setup(file => file.FileName).Returns("photo.jpg");
        fileMock.Setup(file => file.ContentType).Returns("image/jpeg");
        fileMock.Setup(file => file.OpenReadStream()).Returns(stream);

        var attachment = new MediaAttachment
                         {
                             Id            = mediaId
                           , OwnerType     = "JournalEntry"
                           , OwnerId       = journalId.ToString("N")
                           , FileName      = "photo.jpg"
                           , ContentType   = "image/jpeg"
                           , FileSizeBytes = 3
                           , CreatedAt     = DateTimeOffset.UtcNow
                           , StoragePath   = "/media/photo.jpg"
                         };

        _journalServiceMock.Setup(service => service.Exists(journalId))
                           .Returns(true);

        _mediaServiceMock.Setup(service => service.AddAttachmentAsync("JournalEntry"
                                                                     , journalId.ToString("N")
                                                                     , "photo.jpg"
                                                                     , "image/jpeg"
                                                                     , It.IsAny<Stream>()
                                                                     , 3))
                         .ReturnsAsync(attachment);

        var actionResult = await _controller.UploadMedia(journalId, fileMock.Object);

        var createdResult = Assert.IsType<CreatedResult>(actionResult.Result);
        var dto           = Assert.IsType<MediaAttachmentDto>(createdResult.Value);

        Assert.Equal(mediaGuid, dto.Id);
        Assert.Equal("photo.jpg", dto.FileName);
    }

    [Fact]
    public async Task ListMedia_ReturnsNotFound_WhenJournalDoesNotExist()
    {
        var journalId = Guid.NewGuid();
        _journalServiceMock.Setup(service => service.Exists(journalId))
                           .Returns(false);

        var actionResult = await _controller.ListMedia(journalId);

        Assert.IsType<NotFoundResult>(actionResult.Result);
    }

    [Fact]
    public async Task ListMedia_ReturnsOkWithMediaAttachmentDtos_WhenJournalExists()
    {
        var journalId = Guid.NewGuid();
        var mediaGuid = Guid.NewGuid();

        var attachments = new List<MediaAttachment>
                          {
                              new()
                              {
                                  Id            = mediaGuid.ToString("N")
                                , OwnerType     = "JournalEntry"
                                , OwnerId       = journalId.ToString("N")
                                , FileName      = "doc.pdf"
                                , ContentType   = "application/pdf"
                                , FileSizeBytes = 1024
                              }
                          };

        _journalServiceMock.Setup(service => service.Exists(journalId))
                           .Returns(true);

        _mediaServiceMock.Setup(service => service.GetAttachmentsAsync("JournalEntry"
                                                                      , journalId.ToString("N")))
                         .ReturnsAsync(attachments);

        var actionResult = await _controller.ListMedia(journalId);

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var dtos     = Assert.IsAssignableFrom<IReadOnlyList<MediaAttachmentDto>>(okResult.Value);

        Assert.Single(dtos);
        Assert.Equal(mediaGuid, dtos[0].Id);
        Assert.Equal("doc.pdf", dtos[0].FileName);
    }
}
