using CognitivePlatform.Api.Governance;
using CP.Shared.Primitives.Avails.Extensions;
using Microsoft.AspNetCore.Mvc;

namespace CognitivePlatform.Api.Controllers.Admin;

[Route("api/admin/capabilities/{capabilityId}/maturity")]
public sealed class AdminCapabilityMaturityController : AdminControllerBase
{
    private readonly ICapabilityMaturityService _service;

    public AdminCapabilityMaturityController( IConfiguration                configuration
                                            , ICapabilityMaturityService service )
        : base(configuration)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> Get(string capabilityId, CancellationToken cancellationToken)
    {
        if (IsAdminAuthorized().Not()) return Unauthorized401();
        var record = await _service.GetAsync(capabilityId, cancellationToken);
        return record is null ? NotFound() : Ok(record);
    }

    [HttpPost("proposal")]
    public Task<IActionResult> Propose( string                           capabilityId
                                      , CapabilityMaturityApprovalRequest request
                                      , CancellationToken                  cancellationToken )
    {
        return ExecuteAsync(() => _service.ProposeAsync(capabilityId, request.Evidence, request.ApprovedBy, cancellationToken));
    }

    [HttpPost("promotion/{targetLevel}")]
    public Task<IActionResult> Promote( string                           capabilityId
                                      , CapabilityMaturityLevel           targetLevel
                                      , CapabilityMaturityApprovalRequest request
                                      , CancellationToken                  cancellationToken )
    {
        return ExecuteAsync(() => _service.PromoteAsync(capabilityId, targetLevel, request.Evidence, request.ApprovedBy, cancellationToken));
    }

    [HttpPost("quarantine")]
    public Task<IActionResult> Quarantine( string                            capabilityId
                                         , CapabilityMaturityQuarantineRequest request
                                         , CancellationToken                   cancellationToken )
    {
        return ExecuteAsync(() => _service.QuarantineAsync(capabilityId, request.Reason, request.ApprovedBy, cancellationToken));
    }

    private async Task<IActionResult> ExecuteAsync(Func<Task<CapabilityMaturityRecord>> action)
    {
        if (IsAdminAuthorized().Not()) return Unauthorized401();

        try
        {
            return Ok(await action());
        }
        catch (CapabilityMaturityPolicyException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }
}
