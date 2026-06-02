using CognitivePlatform.Api.Domains.BrainDump;
using Microsoft.AspNetCore.Mvc;

namespace CognitivePlatform.Api.Controllers;

[ApiController]
[Route("api/braindumps")]
public sealed class BrainDumpController : ControllerBase
{
    private readonly IBrainDumpService _service;

    public BrainDumpController(IBrainDumpService service)
    {
        _service = service;
    }

    // POST /api/braindumps
    [HttpPost]
    public ActionResult<BrainDumpSession> StartSession()
    {
        var session = _service.StartSession();
        return CreatedAtAction(nameof(GetById), new { id = session.Id }, session);
    }

    // GET /api/braindumps
    [HttpGet]
    public ActionResult<IReadOnlyList<BrainDumpSession>> List([FromQuery] int count = 10)
    {
        var sessions = _service.ListSessions(count);
        return Ok(sessions);
    }

    // GET /api/braindumps/{id}
    [HttpGet("{id}")]
    public ActionResult<BrainDumpSession> GetById(string id)
    {
        var session = _service.GetSession(id);
        if (session is null || session.IsDeleted)
            return NotFound();

        return Ok(session);
    }

    // PATCH /api/braindumps/{id}
    [HttpPatch("{id}")]
    public ActionResult<BrainDumpSession> Update(string id, [FromBody] UpdateBrainDumpRequest request)
    {
        var session = _service.GetSession(id);
        if (session is null || session.IsDeleted)
            return NotFound();

        ApplyUpdates(id, request);

        var updated = _service.GetSession(id);
        return Ok(updated);
    }

    // POST /api/braindumps/{id}/process
    [HttpPost("{id}/process")]
    public ActionResult<BrainDumpSession> MarkProcessed(string id, [FromBody] ProcessBrainDumpRequest request)
    {
        var result = _service.MarkProcessed( id
                                           , request.ExtractionSummary
                                           , request.ExtractedTaskIds
                                           , request.ExtractedInsightIds );

        if (result is null)
            return NotFound();

        return Ok(result);
    }

    // DELETE /api/braindumps/{id}
    [HttpDelete("{id}")]
    public IActionResult Delete(string id)
    {
        var session = _service.GetSession(id);
        if (session is null || session.IsDeleted)
            return NotFound();

        _service.Delete(id);
        return NoContent();
    }

    private void ApplyUpdates(string id, UpdateBrainDumpRequest request)
    {
        if (request.Avoidance is not null)
            _service.UpdateCategory(id, BrainDumpCategory.Avoidance, request.Avoidance);

        if (request.Fears is not null)
            _service.UpdateCategory(id, BrainDumpCategory.Fears, request.Fears);

        if (request.Frustrations is not null)
            _service.UpdateCategory(id, BrainDumpCategory.Frustrations, request.Frustrations);

        if (request.Discouragements is not null)
            _service.UpdateCategory(id, BrainDumpCategory.Discouragements, request.Discouragements);

        if (request.GoalsAndBarriers is not null)
            _service.UpdateCategory(id, BrainDumpCategory.GoalsAndBarriers, request.GoalsAndBarriers);

        if (request.HurtAndSorrow is not null)
            _service.UpdateCategory(id, BrainDumpCategory.HurtAndSorrow, request.HurtAndSorrow);

        if (request.SelfCriticism is not null)
            _service.UpdateCategory(id, BrainDumpCategory.SelfCriticism, request.SelfCriticism);
    }
}

public record UpdateBrainDumpRequest
{
    public string? Avoidance        { get; init; }
    public string? Fears            { get; init; }
    public string? Frustrations     { get; init; }
    public string? Discouragements  { get; init; }
    public string? GoalsAndBarriers { get; init; }
    public string? HurtAndSorrow    { get; init; }
    public string? SelfCriticism    { get; init; }
}

public record ProcessBrainDumpRequest
{
    public string?              ExtractionSummary   { get; init; }
    public IReadOnlyList<string> ExtractedTaskIds   { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ExtractedInsightIds { get; init; } = Array.Empty<string>();
}
