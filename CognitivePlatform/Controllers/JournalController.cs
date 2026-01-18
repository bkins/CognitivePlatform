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
        
        return Ok(new JournalEntryDto
                  {
                          Id        = new Guid(entryRevision.Entry.Id),
                          Text      = entryRevision.LatestRevision.Text,
                          CreatedAt = entryRevision.Entry.CreatedUtc,
                          Tags      = tags
                  });

    }
    [HttpGet]
    public ActionResult<IReadOnlyList<JournalEntryWithRevision>> Get()
    {
        var entryRevision = _journalService.ListEntries();

        return Ok(entryRevision);

    }
}
