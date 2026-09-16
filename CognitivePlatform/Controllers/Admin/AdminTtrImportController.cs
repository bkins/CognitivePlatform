using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Domains.Journal.Import.Ttr;
using Microsoft.AspNetCore.Mvc;

namespace CognitivePlatform.Api.Controllers.Admin;

[Route("api/admin/journal/import/ttr")]
public sealed class AdminTtrImportController : AdminControllerBase
{
    private readonly TtrImportPlanner  _planner;
    private readonly TtrImportExecutor _executor;
    private readonly IObjectStore      _store;
    private readonly TtrEmbeddingReindexService _reindex;

    public AdminTtrImportController(IConfiguration configuration
                                  , TtrImportPlanner planner
                                  , TtrImportExecutor executor
                                  , IObjectStore store
                                  , TtrEmbeddingReindexService reindex)
        : base(configuration)
    {
        _planner  = planner;
        _executor = executor;
        _store    = store;
        _reindex  = reindex;
    }

    [HttpPost("plan")]
    public async Task<IActionResult> Plan([FromBody] TtrImportPlanRequest request, CancellationToken cancellationToken)
    {
        if (!IsAdminAuthorized()) return Unauthorized401();
        try { return Ok(await _planner.PlanAsync(request, cancellationToken)); }
        catch (TtrImportValidationException exception) { return BadRequest(new { error = exception.Message }); }
    }

    [HttpPost("execute")]
    public async Task<IActionResult> Execute([FromBody] TtrExecuteApiRequest request, CancellationToken cancellationToken)
    {
        if (!IsAdminAuthorized()) return Unauthorized401();
        try
        {
            var freshPlan = await _planner.PlanAsync(request.Source, cancellationToken);
            return Ok(await _executor.ExecuteAsync(request.Source, freshPlan, request.Execution, cancellationToken));
        }
        catch (TtrImportValidationException exception) { return BadRequest(new { error = exception.Message }); }
    }

    [HttpGet("batches")]
    public IActionResult GetBatches()
    {
        if (!IsAdminAuthorized()) return Unauthorized401();
        return Ok(_store.List<TtrImportBatch>().OrderByDescending(batch => batch.StartedUtc));
    }

    [HttpGet("batches/{batchId}/items")]
    public IActionResult GetBatchItems(string batchId)
    {
        if (!IsAdminAuthorized()) return Unauthorized401();
        return Ok(_store.List<TtrImportItem>().Where(item => item.BatchId == batchId).OrderBy(item => item.SourceEntryId));
    }

    [HttpPost("batches/{batchId}/reindex")]
    public async Task<IActionResult> Reindex(string batchId, CancellationToken cancellationToken)
    {
        if (!IsAdminAuthorized()) return Unauthorized401();
        try { return Ok(await _reindex.ReindexAsync(batchId, cancellationToken)); }
        catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException)
        {
            return BadRequest(new { error = exception.Message });
        }
    }
}
