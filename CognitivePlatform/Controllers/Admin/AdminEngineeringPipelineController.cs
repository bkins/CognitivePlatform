using CognitivePlatform.Api.Automation;
using CP.Shared.Primitives.Avails.Extensions;
using Microsoft.AspNetCore.Mvc;

namespace CognitivePlatform.Api.Controllers.Admin;

[Route("api/admin/engineering-runs")]
public sealed class AdminEngineeringPipelineController : AdminControllerBase
{
    private readonly IEngineeringPipelineService _service;

    public AdminEngineeringPipelineController( IConfiguration              configuration
                                             , IEngineeringPipelineService service )
        : base(configuration)
    {
        _service = service;
    }

    [HttpPost]
    public Task<IActionResult> Create(CreateEngineeringRunRequest request, CancellationToken cancellationToken)
    {
        return ExecuteAsync(() => _service.CreateAsync(request, cancellationToken));
    }

    [HttpGet("{engineeringRunId}")]
    public async Task<IActionResult> Get(string engineeringRunId, CancellationToken cancellationToken)
    {
        if (IsAdminAuthorized().Not()) return Unauthorized401();
        var record = await _service.GetAsync(engineeringRunId, cancellationToken);
        return record is null ? NotFound() : Ok(record);
    }

    [HttpPost("{engineeringRunId}/workspace")]
    public Task<IActionResult> ProvisionWorkspace(string engineeringRunId, CancellationToken cancellationToken)
    {
        return ExecuteAsync(() => _service.ProvisionWorkspaceAsync(engineeringRunId, cancellationToken));
    }

    [HttpPost("{engineeringRunId}/usage")]
    public Task<IActionResult> RecordUsage( string                    engineeringRunId
                                           , EngineeringExecutionUsage usage
                                           , CancellationToken         cancellationToken )
    {
        return ExecuteAsync(() => _service.RecordUsageAsync(engineeringRunId, usage, cancellationToken));
    }

    private async Task<IActionResult> ExecuteAsync(Func<Task<EngineeringRecord>> action)
    {
        if (IsAdminAuthorized().Not()) return Unauthorized401();

        try
        {
            return Ok(await action());
        }
        catch (EngineeringPipelineException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }
}
