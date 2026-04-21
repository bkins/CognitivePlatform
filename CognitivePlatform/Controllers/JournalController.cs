using CognitivePlatform.Api.Domains.Journal;
using CognitivePlatform.Api.Domains.Journal.Interfaces;
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

    public JournalController (IJournalService            journalService
                            , IJournalRevisionRepository journalRevisionRepository)
    {
        _journalService            = journalService;
        _journalRevisionRepository = journalRevisionRepository;
    }

    [HttpGet("{id:guid}")]
    public ActionResult<JournalEntryDto> GetById(Guid id, CancellationToken ct)
    {
        var entryRevision = _journalService.GetById(id.ToString("N"));
        var tags = entryRevision.LatestRevision.Tags is { Count: > 0 }
                           ? entryRevision.LatestRevision.Tags
                           : Array.Empty<string>();

            var journalEntry = new JournalEntryDto
                               {
                                       Id        = entryRevision.Entry.Id.ToGuid()
                                     , Text      = entryRevision.LatestRevision.Text
                                     , CreatedAt = entryRevision.Entry.CreatedUtc
                                     , Tags      = tags
                                     , Mood      = entryRevision.LatestRevision.Mood
                                     , MoodScore = entryRevision.LatestRevision.MoodScore
                                     , State     = entryRevision.LatestRevision.State

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
                                                       RevisionId = new Guid(revision.RevisionId) 
                                                     , CreatedAt  = revision.CreatedUtc
                                                     , Text       = revision.Text
                                                     , Tags       = revision.Tags
                                                     , Mood       = revision.Mood
                                                     , MoodScore  = revision.MoodScore
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


}
