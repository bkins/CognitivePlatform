using CognitivePlatform.Api.Interpreter;
using CognitivePlatform.Api.Telemetry;
using CognitivePlatform.Api.Telemetry.Events;
using Microsoft.AspNetCore.Mvc;

namespace CognitivePlatform.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SystemController : ControllerBase
{
    private readonly ITelemetrySink      _telemetry;
    private readonly IGroqUsageTracker   _usageTracker;

    public SystemController( ITelemetrySink     telemetrySink
                           , IGroqUsageTracker  usageTracker )
    {
        _telemetry    = telemetrySink;
        _usageTracker = usageTracker;
    }

    [HttpGet("environment")]
    public IActionResult Get()
    {
        _telemetry.Track(new SystemControllerEvent
        {
            Message = "Environment endpoint was hit"
        });
        
        return Ok(new { Pong = "Pong" });
    }

    /// <summary>
    /// Returns the most recent Groq rate-limit snapshot captured from
    /// response headers. Returns an empty snapshot with HasData=false
    /// if no Groq call has been made yet this session.
    /// </summary>
    [HttpGet("usage")]
    public IActionResult GetUsage()
    {
        var snapshot = _usageTracker.Current;

        var response = new
        {
                HasData          = snapshot.HasData
              , CapturedAt       = snapshot.CapturedAt

              , Requests = new
                           {
                                   Limit            = snapshot.RequestLimit
                                 , Remaining        = snapshot.RequestsRemaining
                                 , Used             = snapshot.RequestsUsed
                                 , UsagePercent     = snapshot.RequestUsagePercent
                                 , ResetRaw         = snapshot.RequestsResetRaw
                                 , ResetApproxLocal = FormatResetTime(snapshot.RequestsResetRaw
                                                                    , snapshot.RequestsResetAt)
                           }

              , Tokens = new
                         {
                                 Limit            = snapshot.TokenLimit
                               , Remaining        = snapshot.TokensRemaining
                               , Used             = snapshot.TokensUsed
                               , UsagePercent     = snapshot.TokenUsagePercent
                               , ResetRaw         = snapshot.TokensResetRaw
                               , ResetApproxLocal = FormatResetTime(snapshot.TokensResetRaw
                                                                   , snapshot.TokensResetAt)
                         }
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
                                               , { "Resets", $"{response.Requests.ResetApproxLocal} (Requests), {response.Tokens.ResetApproxLocal} (Tokens)" }
                                         }
                          };
        
        _telemetry.Track(systemEvent);

        return Ok(response);
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
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        if (resetAt is null)
            return raw;

        var localTime = resetAt.Value.ToLocalTime().ToString("h:mm tt");
        return $"{raw} (~{localTime})";
    }
}