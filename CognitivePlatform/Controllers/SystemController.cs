using CognitivePlatform.Api.Interpreter;
using CognitivePlatform.Api.SystemInfo;
using CognitivePlatform.Api.Telemetry;
using CognitivePlatform.Api.Telemetry.Events;
using CP.Shared.Primitives.Avails.Extensions;
using Microsoft.AspNetCore.Mvc;

namespace CognitivePlatform.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SystemController : ControllerBase
{
    private readonly ITelemetrySink              _telemetry;
    private readonly IGroqUsageTracker           _usageTracker;
    private readonly ITelemetryAggregatorService _telemetryAggregator;
    private readonly SystemService               _systemService;
    private readonly ILlmUsageAggregator         _llmUsageAggregator;
    private readonly ILlmRateLimiter             _llmRateLimiter;

    public SystemController( ITelemetrySink              telemetrySink
                           , IGroqUsageTracker           usageTracker
                           , ITelemetryAggregatorService telemetryAggregator
                           , SystemService               systemService
                           , ILlmUsageAggregator         llmUsageAggregator
                           , ILlmRateLimiter             llmRateLimiter )
    {
        _telemetry           = telemetrySink;
        _usageTracker        = usageTracker;
        _telemetryAggregator = telemetryAggregator;
        _systemService       = systemService;
        _llmUsageAggregator  = llmUsageAggregator;
        _llmRateLimiter      = llmRateLimiter;
    }


    [HttpGet("ping")]
    public IActionResult Ping()
    {
        _telemetry.Track(new SystemControllerEvent
        {
            Message = "Ping endpoint was hit"
        });
        
        return Ok(new { Pong = "Pong" });
    }
    
    [HttpGet("environment")]
    public IActionResult Get()
    {
        var systemEvent = new SystemControllerEvent
                          {
                                  Message = "Environment endpoint was hit"
                                , Data = new Dictionary<string, object?>
                                         {
                                                 { "Environment", _systemService.GetEnvironment() }
                                               , { "Version", _systemService.GetVersion() }
                                         }
                          };
        
        _telemetry.Track(systemEvent);
        
        return Ok(systemEvent);
    }

    /// <summary>
    /// Returns the most recent Groq rate-limit snapshot captured from
    /// response headers. Returns an empty snapshot with HasData=false
    /// if no Groq call has been made yet this session.
    /// </summary>
    [HttpGet("usage")]
    public IActionResult GetUsage()
    {
        var snapshot        = _usageTracker.Current;
        var sessionTotals   = _llmUsageAggregator.GetTotals();
        var groqRateLimits  = _llmRateLimiter.GetCurrentSnapshot("Groq");

        var response = new
                       {
                               HasData    = snapshot.HasData
                             , CapturedAt = snapshot.CapturedAt

                             , Requests = new
                                          {
                                                  Limit        = snapshot.RequestLimit
                                                , Remaining    = snapshot.RequestsRemaining
                                                , Used         = snapshot.RequestsUsed
                                                , UsagePercent = snapshot.RequestUsagePercent
                                                , ResetRaw     = snapshot.RequestsResetRaw
                                                , ResetApproxLocal = FormatResetTime(snapshot.RequestsResetRaw
                                                                                   , snapshot.RequestsResetAt)
                                          }

                             , Tokens = new
                                        {
                                                Limit        = snapshot.TokenLimit
                                              , Remaining    = snapshot.TokensRemaining
                                              , Used         = snapshot.TokensUsed
                                              , UsagePercent = snapshot.TokenUsagePercent
                                              , ResetRaw     = snapshot.TokensResetRaw
                                              , ResetApproxLocal = FormatResetTime(snapshot.TokensResetRaw
                                                                                 , snapshot.TokensResetAt)
                                        }

                             , SessionTotals = new
                                               {
                                                       PromptTokens     = sessionTotals.PromptTokens
                                                     , CompletionTokens = sessionTotals.CompletionTokens
                                                     , TotalTokens      = sessionTotals.TotalTokens
                                               }

                             , GroqRateLimitResetAt = groqRateLimits.HasData
                                                              ? FormatResetTime(groqRateLimits.RequestsResetRaw
                                                                              , groqRateLimits.RequestsResetAt)
                                                              : null
                       };

        var systemEvent = new SystemControllerEvent
                          {
                                  Message = "Usage endpoint was hit"
                                , Data = new Dictionary<string, object?>
                                         {
                                                 { "HasData", snapshot.HasData }
                                               , { "CapturedAt", snapshot.CapturedAt }
                                               , { "RequestsRemaining", snapshot.RequestsRemaining }
                                               , { "TokensRemaining", snapshot.TokensRemaining }
                                               , { "Resets", $"{response.Requests.ResetApproxLocal} (Requests)"
                                                           + $", {response.Tokens.ResetApproxLocal} (Tokens)" }
                                               , { "SessionTotalTokens", sessionTotals.TotalTokens }
                                         }
                          };

        _telemetry.Track(systemEvent);

        return Ok(response);
    }

    /// <summary>
    /// Returns aggregated telemetry metrics grouped by operation name.
    /// Returns an empty list until a structured event store is wired up.
    /// </summary>
    [HttpGet("telemetry")]
    public async Task<IActionResult> GetTelemetry()
    {
        var metrics = await _telemetryAggregator.GetAggregatedMetricsAsync();

        _telemetry.Track(new SystemControllerEvent
                         {
                             Message = "Telemetry endpoint was hit"
                         });

        return Ok(metrics);
    }

    // ----------------------------------------------------------------
    // Helpers
    // ----------------------------------------------------------------

    /// <summary>
    /// Formats the reset time as "1m30s (~7:13 PM)" per the UI contract.
    /// Returns an empty string when no data is available.
    /// </summary>
    private static string FormatResetTime(string raw, DateTimeOffset? resetAt)
    {
        if (raw.HasNoValue())
            return string.Empty;

        if (resetAt is null)
            return raw;

        var localTime = resetAt.Value.ToLocalTime().ToString("h:mm tt");
        
        return $"{raw} (~{localTime})";
    }
}