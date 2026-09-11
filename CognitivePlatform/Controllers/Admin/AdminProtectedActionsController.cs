using CognitivePlatform.Api.Automation;
using CP.Shared.Primitives.Avails.Extensions;
using Microsoft.AspNetCore.Mvc;

namespace CognitivePlatform.Api.Controllers.Admin;

[Route("api/admin/protected-actions")]
public sealed class AdminProtectedActionsController : AdminControllerBase
{
    private readonly IProtectedActionApprovalService _service;

    public AdminProtectedActionsController( IConfiguration                      configuration
                                          , IProtectedActionApprovalService service )
        : base(configuration)
    {
        _service = service;
    }

    [HttpPost]
    public Task<IActionResult> Create(ProtectedActionRequest request, CancellationToken cancellationToken)
    {
        return ExecuteAsync(() => _service.RequestAsync(request, cancellationToken));
    }

    [HttpGet("{actionRequestId}")]
    public async Task<IActionResult> Get(string actionRequestId, CancellationToken cancellationToken)
    {
        if (IsAdminAuthorized().Not()) return Unauthorized401();
        var record = await _service.GetAsync(actionRequestId, cancellationToken);
        return record is null ? NotFound() : Ok(record);
    }

    [HttpPost("{actionRequestId}/approve")]
    public Task<IActionResult> Approve( string                          actionRequestId
                                       , ProtectedActionApprovalRequest request
                                       , CancellationToken               cancellationToken )
    {
        return ExecuteAsync(() => _service.ApproveAsync(actionRequestId, request.ApprovedBy, request.Reason, request.RollbackPlan, cancellationToken));
    }

    [HttpPost("{actionRequestId}/require-approval")]
    public Task<IActionResult> RequireApproval(string actionRequestId, CancellationToken cancellationToken)
    {
        return ExecuteAsync(() => _service.RequireApprovedForExecutionAsync(actionRequestId, cancellationToken));
    }

    private async Task<IActionResult> ExecuteAsync(Func<Task<ProtectedActionRequestRecord>> action)
    {
        if (IsAdminAuthorized().Not()) return Unauthorized401();

        try
        {
            return Ok(await action());
        }
        catch (ProtectedActionPolicyException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }
}
