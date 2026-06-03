using CognitivePlatform.Api.Training;
using CP.Shared.Primitives.Avails.Extensions;
using Microsoft.AspNetCore.Mvc;

namespace CognitivePlatform.Api.Controllers.Admin;

[Route("api/admin/training")]
public sealed class AdminTrainingController : AdminControllerBase
{
    private readonly IInterpreterTrainingStore _store;

    public AdminTrainingController( IConfiguration             configuration
                                  , IInterpreterTrainingStore  store )
        : base(configuration)
    {
        _store = store;
    }

    /// <summary>
    /// Returns total corpus size and the 10 most recent training records.
    /// </summary>
    [HttpGet("corpus")]
    public async Task<IActionResult> GetCorpus(CancellationToken ct)
    {
        if (IsAdminAuthorized().Not()) return Unauthorized401();

        var count  = await _store.GetCountAsync(ct);
        var recent = await _store.GetRecentAsync(10, ct);

        return Ok(new { count, recent });
    }
}
