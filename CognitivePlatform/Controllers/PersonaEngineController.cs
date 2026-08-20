using CognitivePlatform.Api.Domains.PersonaEngine;
using CognitivePlatform.Api.Domains.PersonaEngine.Models;
using Microsoft.AspNetCore.Mvc;

namespace CognitivePlatform.Api.Controllers;

[ApiController]
[Route("api/persona-engine")]
public sealed class PersonaEngineController : ControllerBase
{
    private readonly IPersonaEngine _engine;

    public PersonaEngineController(IPersonaEngine engine)
    {
        _engine = engine;
    }

    [HttpPost("resolve")]
    public async Task<ActionResult<PersonaContextResult>> Resolve([FromBody] ResolvePersonaRequest request
                                                                , CancellationToken               ct)
    {
        if ((request?.Message).HasNoValue())
            return BadRequest("Message is required.");

        var result = await _engine.ResolveAsync(request.Message, ct).ConfigureAwait(false);

        return Ok(result);
    }
}

public record ResolvePersonaRequest(string Message);
