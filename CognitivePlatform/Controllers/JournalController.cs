using CognitivePlatform.Api.Domains.Journal;
using CognitivePlatform.Api.Domains.Journal.Interfaces;
using CognitivePlatform.Api.Domains.Media;
using CognitivePlatform.Api.Models;
using CognitivePlatform.Api.Models.TestingTemp;
using CP.Shared.Primitives.Avails.Extensions;
using Microsoft.AspNetCore.Mvc;

namespace CognitivePlatform.Api.Controllers;

//TODO: Consider renaming `journalId` to `entryId`, here and everywhere...for consistency

[ApiController]
[Route("api/journals")]
public sealed class JournalController : ControllerBase
{
    private readonly IJournalService            _journalService;
    private readonly IJournalRevisionRepository _journalRevisionRepository;
    private readonly IMediaAttachmentService    _mediaService;

    public JournalController (IJournalService            journalService
                            , IJournalRevisionRepository journalRevisionRepository
                            , IMediaAttachmentService    mediaService)
    {
        _journalService            = journalService;
        _journalRevisionRepository = journalRevisionRepository;
        _mediaService              = mediaService;
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<JournalEntryDto>> GetById(Guid id, CancellationToken ct)
    {
        var entryRevision = _journalService.GetById(id.ToString("N"));
        var tags          = entryRevision.LatestRevision.Tags is { Count: > 0 }
                                    ? entryRevision.LatestRevision.Tags
                                    : Array.Empty<string>();

        var attachmentCount = await _mediaService.GetAttachmentCountAsync("JournalEntry"
                                                                         , id.ToString("N"));

        var journalEntry = new JournalEntryDto
                           {
                               Id              = entryRevision.Entry.Id.ToGuid()
                             , Text            = entryRevision.LatestRevision.Text
                             , CreatedAt       = entryRevision.Entry.CreatedUtc
                             , Tags            = tags
                             , Mood            = entryRevision.LatestRevision.Mood
                             , MoodScore       = entryRevision.LatestRevision.MoodScore
                             , State           = entryRevision.LatestRevision.State
                             , IsEdited        = entryRevision.IsEdited
                             , AttachmentCount = attachmentCount
                             , ValenceEmoji    = EmojiNormalizationService.MapValenceEmoji(entryRevision.LatestRevision.MoodScore)
                             , AffectEmoji     = EmojiNormalizationService.MapAffectEmoji(entryRevision.LatestRevision.Mood)
                           };
        return Ok(journalEntry);
    }
    
    [HttpGet]
    public ActionResult<IReadOnlyList<JournalEntryWithRevision>> Get()
    {
        var entryRevision = _journalService.ListEntries();

        return Ok(entryRevision);

    }
    
    //GET /api/journals/{journalId}/revisions
    /*This endpoint never returns the current revision.
     * The current revision already has a home (GetById)
     * Revision history returns the full immutable revision timeline.
     *
     * Case 1 — Entry was never edited:
     *  - [ initial revision ]
     *
     * Case 2 — Journal does not exist:
     *  - 404 Not Found
     *
     * Case 3 — Journal exists but is deleted:
     *  - Still return revisions (read-only)
     */

    [HttpGet("{journalId:guid}/revisions")]
    public ActionResult<IReadOnlyList<JournalRevisionDto>> GetRevisions(Guid journalId)
    {
        var journalExists = _journalService.Exists(journalId);
        if (journalExists.Not()) return NotFound();

        var revisions = _journalRevisionRepository.GetRevisionsByEntryId(journalId.ToString("N"));

        var dto = revisions.Select(revision => new JournalRevisionDto
                                               {
                                                       RevisionId   = new Guid(revision.RevisionId) 
                                                     , CreatedAt    = revision.CreatedUtc
                                                     , Text         = revision.Text
                                                     , Tags         = revision.Tags
                                                     , Mood         = revision.Mood
                                                     , MoodScore    = revision.MoodScore
                                                     , ValenceEmoji = EmojiNormalizationService.MapValenceEmoji(revision.MoodScore)
                                                     , AffectEmoji  = EmojiNormalizationService.MapAffectEmoji(revision.Mood)
                                               });

        return Ok(dto);
    }
    
    // POST /api/journals/{journalId:guid}/edit-test
    [ApiExplorerSettings(GroupName = "dev-only")]
    [HttpPost("{journalId:guid}/edit-test")]
    public ActionResult EditEntry_Test(Guid                               journalId
                                      , [FromBody] JournalEditTestRequest request)
    {
        /*
         * TEST-ONLY ENDPOINT
         * Purpose: force creation of journal revisions during development.
         * This endpoint will be removed once real edit UX exists.
         */

        try
        {
            var temp = _journalService.EditEntry(journalId.ToString("N")
                                               , text: request.Text
                                               , tags: request.Tags
                                               , mood: request.Mood
                                               , moodScore: request.MoodScore);

            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    // POST /api/journals/{journalId}/media
    [HttpPost("{journalId:guid}/media")]
    public async Task<ActionResult<MediaAttachmentDto>> UploadMedia(Guid journalId, IFormFile file)
    {
        if (_journalService.Exists(journalId).Not())
            return NotFound();

        if (file is null || file.Length == 0)
            return BadRequest("No file provided.");

        await using var stream = file.OpenReadStream();

        var attachment = await _mediaService.AddAttachmentAsync("JournalEntry"
                                                              , journalId.ToString("N")
                                                              , file.FileName
                                                              , file.ContentType ?? "application/octet-stream"
                                                              , stream
                                                              , file.Length);
        return Created($"/api/media/{attachment.Id}", ToMediaDto(attachment));
    }

    // GET /api/journals/{journalId}/media
    [HttpGet("{journalId:guid}/media")]
    public async Task<ActionResult<IReadOnlyList<MediaAttachmentDto>>> ListMedia(Guid journalId)
    {
        if (_journalService.Exists(journalId).Not())
            return NotFound();

        var attachments = await _mediaService.GetAttachmentsAsync("JournalEntry"
                                                                 , journalId.ToString("N"));
        return Ok(attachments.Select(ToMediaDto).ToList());
    }

    private static MediaAttachmentDto ToMediaDto( MediaAttachment attachment )
        => new()
           {
                   Id            = attachment.Id.ToGuid()
                 , OwnerType     = attachment.OwnerType
                 , OwnerId       = attachment.OwnerId.ToGuid()
                 , FileName      = attachment.FileName
                 , ContentType   = attachment.ContentType
                 , FileSizeBytes = attachment.FileSizeBytes
                 , CreatedAt     = attachment.CreatedAt
                 , StoragePath   = attachment.StoragePath
           };
}
