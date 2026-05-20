using CognitivePlatform.Api.Domains.Identity;
using Microsoft.AspNetCore.Mvc;

namespace CognitivePlatform.Api.Controllers;

[ApiController]
[Route("api/identity")]
public sealed class IdentityController : ControllerBase
{
    private readonly IIdentityService _service;

    public IdentityController(IIdentityService service)
    {
        _service = service;
    }

    [HttpGet("profile")]
    public async Task<ActionResult<PersonProfile>> GetProfile(CancellationToken ct)
    {
        var profile = await _service.GetProfileAsync(ct);
        return Ok(profile);
    }

    [HttpPut("profile")]
    public async Task<ActionResult<PersonProfile>> UpdateProfile( [FromBody]       PersonProfile  profile
                                                                 , CancellationToken               ct )
    {
        await _service.UpdateProfileAsync(profile, ct);
        return Ok(profile);
    }

    [HttpGet("assertions")]
    public async Task<ActionResult<IReadOnlyList<IdentityAssertion>>> GetAssertions(CancellationToken ct)
    {
        var assertions = await _service.GetAssertionsAsync(ct);
        return Ok(assertions);
    }

    [HttpPost("assertions")]
    public async Task<ActionResult<IdentityAssertion>> AddAssertion( [FromBody]       IdentityAssertion  assertion
                                                                    , CancellationToken                   ct )
    {
        await _service.AddAssertionAsync(assertion, ct);
        return Ok(assertion);
    }

    [HttpPut("assertions/{id}/confirm")]
    public async Task<IActionResult> ConfirmAssertion( [FromRoute] string            id
                                                      , CancellationToken             ct )
    {
        await _service.ConfirmAssertionAsync(id, ct);
        return Ok($"Assertion '{id}' confirmed.");
    }
}
