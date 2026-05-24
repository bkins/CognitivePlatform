using CognitivePlatform.Api.Domains.Personas;
using CognitivePlatform.Api.Domains.Personas.Models;
using Microsoft.AspNetCore.Mvc;

namespace CognitivePlatform.Api.Controllers;

[ApiController]
[Route("api/persona")]
public sealed class PersonaController : ControllerBase
{
    private readonly IPersonaService            _service;
    private readonly IPersonaStore              _store;
    private readonly IPersonaSessionManager     _sessionManager;
    private readonly IMemoryConfirmationQueue   _confirmationQueue;
    private readonly IPersonaStabilityTracker?  _stabilityTracker;

    public PersonaController( IPersonaService           service
                            , IPersonaStore              store
                            , IPersonaSessionManager     sessionManager
                            , IMemoryConfirmationQueue   confirmationQueue
                            , IPersonaStabilityTracker?  stabilityTracker  = null )
    {
        _service          = service           ?? throw new ArgumentNullException(nameof(service));
        _store            = store             ?? throw new ArgumentNullException(nameof(store));
        _sessionManager   = sessionManager    ?? throw new ArgumentNullException(nameof(sessionManager));
        _confirmationQueue = confirmationQueue ?? throw new ArgumentNullException(nameof(confirmationQueue));
        _stabilityTracker  = stabilityTracker;
    }

    [HttpPost]
    public async Task<ActionResult<CanonicalPersona>> Create( [FromBody]       CreatePersonaRequest request
                                                            , CancellationToken                     ct )
    {
        var persona = await _service.CreateAsync(request.Name, request.ScenarioDescription, ct);
        return Ok(persona);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CanonicalPersona>>> List(CancellationToken ct)
    {
        var personas = await _service.ListAsync(ct);
        return Ok(personas);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CanonicalPersona>> GetById( [FromRoute] Guid              id
                                                             , CancellationToken              ct )
    {
        var persona = await _service.GetAsync(id, ct);

        if (persona is null)
            return NotFound($"Persona '{id}' not found.");

        return Ok(persona);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete( [FromRoute] Guid              id
                                           , CancellationToken             ct )
    {
        await _service.DeleteAsync(id, ct);
        return Ok($"Persona '{id}' deleted.");
    }

    [HttpPost("{id:guid}/memory")]
    public async Task<ActionResult<PersonaMemory>> AddMemory( [FromRoute]      Guid              id
                                                            , [FromBody]       AddMemoryRequest  request
                                                            , CancellationToken                  ct )
    {
        var persona = await _service.GetAsync(id, ct);

        if (persona is null)
            return NotFound($"Persona '{id}' not found.");

        var memory = await _service.AddMemoryAsync(id, request.Content, request.Type, request.UserAsserted, ct);
        return Ok(memory);
    }

    [HttpPatch("{id:guid}/memory/{memoryId:guid}/state")]
    public async Task<IActionResult> UpdateMemoryState( [FromRoute]      Guid                    id
                                                      , [FromRoute]      Guid                    memoryId
                                                      , [FromBody]       UpdateMemoryStateRequest request
                                                      , CancellationToken                         ct )
    {
        await _service.UpdateMemoryStateAsync(memoryId, id, request.NewState, ct);
        return Ok($"Memory '{memoryId}' state updated to '{request.NewState}'.");
    }

    [HttpPost("{id:guid}/snapshot")]
    public async Task<ActionResult<MemorySnapshot>> CreateSnapshot( [FromRoute]      Guid                   id
                                                                  , [FromBody]       CreateSnapshotRequest  request
                                                                  , CancellationToken                        ct )
    {
        var persona = await _service.GetAsync(id, ct);

        if (persona is null)
            return NotFound($"Persona '{id}' not found.");

        var snapshot = await _service.CreateSnapshotAsync(id, request.Name, request.Notes, ct);
        return Ok(snapshot);
    }

    [HttpGet("{id:guid}/snapshot")]
    public async Task<ActionResult<IReadOnlyList<MemorySnapshot>>> ListSnapshots( [FromRoute] Guid              id
                                                                                , CancellationToken              ct )
    {
        var persona = await _service.GetAsync(id, ct);

        if (persona is null)
            return NotFound($"Persona '{id}' not found.");

        var snapshots = await _store.GetSnapshotsAsync(id, ct);

        return Ok(snapshots);
    }

    [HttpGet("active/{conversationId}")]
    public ActionResult<Guid> GetActivePersona([FromRoute] string conversationId)
    {
        var personaId = _sessionManager.GetActivePersona(conversationId);

        if (personaId is null)
            return NoContent();

        return Ok(personaId.Value);
    }

    // ----------------------------------------------------------------
    // Phase C — Memory Reconstruction Engine endpoints
    // ----------------------------------------------------------------

    [HttpPost("{id:guid}/memory/{memoryId:guid}/confirm")]
    public async Task<IActionResult> ConfirmMemory( [FromRoute] Guid              id
                                                  , [FromRoute] Guid              memoryId
                                                  , CancellationToken             ct )
    {
        var persona = await _service.GetAsync(id, ct);

        if (persona is null)
            return NotFound($"Persona '{id}' not found.");

        await _store.ConfirmMemoryAsync(memoryId, id, ct);

        return Ok($"Memory '{memoryId}' confirmed.");
    }

    [HttpPost("{id:guid}/memory/{memoryId:guid}/contradict")]
    public async Task<IActionResult> MarkMemoryContradicted(
        [FromRoute] Guid              id
      , [FromRoute] Guid              memoryId
      , [FromBody]  ContradictMemoryRequest request
      , CancellationToken                   ct )
    {
        var persona = await _service.GetAsync(id, ct);

        if (persona is null)
            return NotFound($"Persona '{id}' not found.");

        await _store.MarkMemoryContradictedAsync(memoryId, id, request.ContradictionMemoryIds, ct);

        return Ok($"Memory '{memoryId}' marked as contradicted.");
    }

    // ----------------------------------------------------------------
    // Phase D — Stability Score endpoint
    // ----------------------------------------------------------------

    [HttpGet("{id:guid}/stability/{conversationId}")]
    public async Task<ActionResult<PersonaStabilityScore>> GetStabilityScore(
        [FromRoute] Guid              id
      , [FromRoute] string            conversationId
      , CancellationToken             ct )
    {
        var persona = await _service.GetAsync(id, ct);

        if (persona is null)
            return NotFound($"Persona '{id}' not found.");

        if (_stabilityTracker is null)
            return StatusCode(503, "Stability tracking is not available.");

        var score = _stabilityTracker.GetScore(conversationId, id);
        return Ok(score);
    }

    [HttpGet("{id:guid}/memory/pending")]
    public async Task<ActionResult<IReadOnlyList<PersonaMemory>>> GetPendingMemories(
        [FromRoute] Guid              id
      , [FromQuery] string?           conversationId
      , CancellationToken             ct )
    {
        var persona = await _service.GetAsync(id, ct);

        if (persona is null)
            return NotFound($"Persona '{id}' not found.");

        if (!string.IsNullOrWhiteSpace(conversationId))
        {
            var queued = _confirmationQueue.Peek(conversationId, count: 10);
            return Ok(queued);
        }

        var allMemories = await _store.GetMemoriesAsync(id, ct);

        var provisional = allMemories
            .Where(memory => memory.State == MemoryState.Provisional)
            .ToList();

        return Ok(provisional);
    }
}
