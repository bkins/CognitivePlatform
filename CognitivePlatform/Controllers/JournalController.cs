using CognitivePlatform.Api.Domains.Journal;
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
        var entry = _journalService.GetById(id);

        if (entry is null)
            return NotFound();
        IReadOnlyList<string> tags = entry.Tags is { Count: > 0 }
                                             ? entry.Tags
                                             : Array.Empty<string>();
        return Ok(new JournalEntryDto
                  {
                          Id        = id
                        , Text      = entry.Text
                        , CreatedAt = entry.CreatedUtc
                        , Tags      = tags
                  });
    }
}
