using CognitivePlatform.Api.Telemetry;
using CP.Shared.Primitives.Avails.Extensions;
using Microsoft.AspNetCore.Mvc;

namespace CognitivePlatform.Api.Controllers.Admin;

[Route("api/admin/logs")]
public sealed class AdminLogsController : AdminControllerBase
{
    private readonly InMemoryLogStore _logStore;

    public AdminLogsController( IConfiguration    configuration
                              , InMemoryLogStore   logStore)
        : base(configuration)
    {
        _logStore = logStore;
    }

    /// <summary>
    /// Returns up to <paramref name="take"/> recent log entries, newest first.
    /// Optionally filtered by <paramref name="level"/> (INF, WRN, ERR …)
    /// and/or a <paramref name="search"/> string matched against message and category.
    /// </summary>
    [HttpGet]
    public IActionResult GetLogs( [FromQuery] int     take   = 200
                                , [FromQuery] string? level  = null
                                , [FromQuery] string? search = null)
    {
        if (IsAdminAuthorized().Not()) return Unauthorized401();

        var entries = _logStore.GetRecent(take, level, search);
        
        return Ok(entries);
    }

    /// <summary>Clears all buffered log entries.</summary>
    [HttpDelete]
    public IActionResult ClearLogs()
    {
        if (IsAdminAuthorized().Not()) return Unauthorized401();

        _logStore.Clear();
        
        return NoContent();
    }
}
