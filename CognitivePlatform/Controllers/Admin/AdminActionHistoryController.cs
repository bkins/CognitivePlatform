using CognitivePlatform.Api.Audit;
using CP.Shared.Primitives.Avails.Extensions;
using Microsoft.AspNetCore.Mvc;

namespace CognitivePlatform.Api.Controllers.Admin;

[Route("api/admin/action-history")]
public sealed class AdminActionHistoryController : AdminControllerBase
{
    private readonly IAuditLog _auditLog;

    public AdminActionHistoryController( IConfiguration configuration
                                       , IAuditLog        auditLog)
        : base(configuration)
    {
        _auditLog = auditLog;
    }

    [HttpGet]
    public IActionResult GetHistory([FromQuery] int take = 100, [FromQuery] AuditOutcome? outcome = null)
    {
        if (IsAdminAuthorized().Not()) return Unauthorized401();

        var entries = _auditLog.List()
                               .Where(entry => outcome is null || entry.Outcome == outcome)
                               .Take(Math.Clamp(take, 1, 200))
                               .Select(entry => new ActionHistoryItemDto(
                                                 entry.ActionName
                                               , entry.Outcome.ToString()
                                               , entry.OccurredUtc
                                               , entry.Parameters.HasValue() ? "Parameters redacted" : null
                                               , RedactSummary(entry.ErrorMessage)))
                               .ToList();
        return Ok(entries);
    }

    private static string? RedactSummary(string? errorMessage)
    {
        if (errorMessage.HasNoValue()) return null;
        return "Execution reported a failure. See secured diagnostics for details.";
    }
}

public sealed record ActionHistoryItemDto(string ActionName, string Outcome, DateTimeOffset OccurredUtc, string? ParameterSummary, string? ExecutionSummary);
