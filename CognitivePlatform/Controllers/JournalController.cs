using CognitivePlatform.Api.Domains.Journal;
using CognitivePlatform.Api.Models;
using Microsoft.AspNetCore.Mvc;

namespace CognitivePlatform.Api.Controllers;

[ApiController]
[Route("api/journals")]
public sealed class JournalController : ControllerBase
{
    private readonly IJournalService _journalService;

    public JournalController(IJournalService journalService)
    {
        _journalService = journalService;
    }

    [HttpGet("{id:guid}")]
    public ActionResult<JournalEntryDto> GetById(Guid id, CancellationToken ct)
    {
        var entryRevision = _journalService.GetById(id.ToString("N"));

        //var entry = _journalService.GetById(id);

        var tags = entryRevision.LatestRevision.Tags is { Count: > 0 }
                           ? entryRevision.LatestRevision.Tags
                           : Array.Empty<string>();

        var journalEntry = new JournalEntryDto
                           {
                                   Id        = new Guid(entryRevision.Entry.Id)
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
     * Revision history should never compete with “current truth”
     *
     * Case 1 — Entry was never edited:
     *  - []
     *
     * Case 2 — Journal does not exist:
     *  - 404 Not Found
     *
     * Case 3 — Journal exists but is deleted:
     *  - Still return revisions (read-only)
     *  - Deletion is about current visibility, not historical truth*/
}
