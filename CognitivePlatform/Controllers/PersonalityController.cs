using CognitivePlatform.Api.Domains.Personality;
using Microsoft.AspNetCore.Mvc;

namespace CognitivePlatform.Api.Controllers;

[ApiController]
[Route("api/personality")]
public sealed class PersonalityController : ControllerBase
{
    private readonly IPersonalityService _service;

    public PersonalityController(IPersonalityService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PersonalityDefinition>>> GetAll()
    {
        var personalities = await _service.GetAllAsync().ConfigureAwait(false);

        return Ok(personalities);
    }

    [HttpGet("active")]
    public async Task<ActionResult<PersonalityDefinition>> GetActive()
    {
        var active = await _service.GetActiveAsync().ConfigureAwait(false);

        if (active is null)
            return NotFound("No active personality is set.");

        return Ok(active);
    }

    [HttpPut("{id}/activate")]
    public async Task<ActionResult<PersonalityDefinition>> Activate([FromRoute] string id)
    {
        try
        {
            var activated = await _service.SetActiveAsync(id).ConfigureAwait(false);

            return Ok(activated);
        }
        catch (KeyNotFoundException)
        {
            return NotFound($"Personality '{id}' not found.");
        }
    }

    [HttpPost]
    public async Task<ActionResult<PersonalityDefinition>> Add([FromBody] PersonalityDefinition personality)
    {
        var added = await _service.AddAsync(personality).ConfigureAwait(false);

        return CreatedAtAction(nameof(GetAll), new { }, added);
    }
}
