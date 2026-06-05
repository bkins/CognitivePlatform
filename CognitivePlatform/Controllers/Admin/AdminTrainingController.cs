using System.Text;
using System.Text.Json;
using CognitivePlatform.Api.Training;
using CP.Shared.Primitives.Avails.Extensions;
using Microsoft.AspNetCore.Mvc;

namespace CognitivePlatform.Api.Controllers.Admin;

[Route("api/admin/training")]
public sealed class AdminTrainingController : AdminControllerBase
{
    private const int ExportDefaultLimit = 1000;
    private const int ExportMaxLimit     = 10000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

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

    /// <summary>
    /// Exports the training corpus as JSONL (one fine-tuning example per line).
    /// Each line: {"prompt":"...","completion":"...","metadata":{...}}
    /// Query params: limit (default 1000, max 10000), succeeded_only (default false).
    /// </summary>
    [HttpGet("corpus/export")]
    public async Task<IActionResult> ExportCorpus( [FromQuery] int  limit         = ExportDefaultLimit
                                                 , [FromQuery(Name = "succeeded_only")] bool succeededOnly = false
                                                 , CancellationToken ct = default)
    {
        if (IsAdminAuthorized().Not()) return Unauthorized401();

        var effectiveLimit = Math.Clamp(limit, 1, ExportMaxLimit);
        var records        = await _store.GetForExportAsync(effectiveLimit, succeededOnly, ct);

        var sb = new StringBuilder(records.Count * 256);
        foreach (var record in records)
        {
            var line = JsonSerializer.Serialize(new
                                                {
                                                    prompt     = record.UserInput
                                                  , completion = record.ModelOutput
                                                  , metadata   = new
                                                                 {
                                                                     action     = record.FinalResolvedAction
                                                                   , succeeded  = record.ExecutionSucceeded
                                                                   , latency_ms = record.LatencyMs
                                                                 }
                                                });
            sb.AppendLine(line);
        }

        return File(Encoding.UTF8.GetBytes(sb.ToString())
                  , "application/x-ndjson"
                  , "training-corpus.jsonl");
    }

    /// <summary>
    /// Returns corpus quality metrics: size by day, action distribution,
    /// overall success rate, and average latency.
    /// </summary>
    [HttpGet("corpus/stats")]
    public async Task<IActionResult> GetCorpusStats(CancellationToken ct)
    {
        if (IsAdminAuthorized().Not()) return Unauthorized401();

        var stats = await _store.GetStatsAsync(ct);

        return Ok(stats);
    }
}
