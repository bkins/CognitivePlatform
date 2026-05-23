using CognitivePlatform.Api.Domains.Personas;
using CognitivePlatform.Api.Domains.Personas.Models;
using Microsoft.AspNetCore.Mvc;

namespace CognitivePlatform.Api.Controllers;

[ApiController]
[Route("api/persona")]
public sealed class PersonaController : ControllerBase
{
    private readonly IPersonaService _service;
    private readonly IPersonaStore   _store;

    public PersonaController( IPersonaService service
                            , IPersonaStore   store )
    {
        _service = service;
        _store   = store;
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
}
