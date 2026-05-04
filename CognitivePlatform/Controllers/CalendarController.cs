using CognitivePlatform.Api.Domains.Calendar;
using CognitivePlatform.Api.Integrations.Calendar;
using CP.Shared.Primitives.Avails.Extensions;
using Microsoft.AspNetCore.Mvc;

namespace CognitivePlatform.Api.Controllers;

/// <summary>
/// Handles the Google Calendar OAuth flow.
///
/// Flow:
///   1. Client calls GET /auth/google/connect  →  browser redirects to Google consent page.
///   2. User approves access.
///   3. Google redirects to GET /auth/google/callback?code=...
///   4. This endpoint exchanges the code for tokens and stores them.
///   5. The client may then use calendar actions via the conversation endpoint.
/// </summary>
[ApiController]
[Route("auth/google")]
public sealed class CalendarController : ControllerBase
{
    private readonly ICalendarProvider _calendar;

    public CalendarController( ICalendarProvider      calendar)
    {
        _calendar = calendar;
    }

    /// <summary>
    /// Initiates the Google Calendar OAuth flow by redirecting the browser to the
    /// Google consent screen.
    /// </summary>
    [HttpGet("connect")]
    public IActionResult Connect()
    {
        var url = _calendar.GetAuthorizationUrl();
        
        if (url.HasNoValue())
            return BadRequest("Google Calendar is not configured. Check that ClientId and ClientSecret are set in user-secrets.");

        return Redirect(url);
    }

    /// <summary>
    /// OAuth callback. Google redirects here after the user approves (or denies) access.
    /// Exchanges the authorisation code for tokens and stores them.
    /// </summary>
    [HttpGet("callback")]
    public async Task<IActionResult> Callback( [FromQuery] string? code
                                             , [FromQuery] string? error )
    {
        if (error is not null)
            return BadRequest($"Google denied access: {error}. Visit /auth/google/connect to try again.");

        if (code?.HasNoValue() ?? true)
            return BadRequest("No authorisation code received.");

        var success = await _calendar.ExchangeCodeAsync(code);

        return success
                   ? Ok("Google Calendar connected successfully. You can close this tab and return to the app.")
                   : StatusCode(500, "Failed to exchange the authorisation code. Please visit /auth/google/connect and try again.");
    }

    /// <summary>
    /// Returns the current connection status.
    /// </summary>
    [HttpGet("status")]
    public IActionResult Status()
        => Ok(new { connected = _calendar.IsConnected });

    [HttpGet("todays-events")]
    public async Task<IActionResult> GetTodaysEvents()
    {
        var badRequestMessage = "Google Calendar is not connected. Please visit /auth/google/connect to connect your calendar.";
        if (_calendar.IsConnected.Not())
        {
            return BadRequest(badRequestMessage);
        }
        
        var today  = DateTimeOffset.UtcNow.Date; // Start of today in UTC
        var from   = new DateTimeOffset(today,            TimeSpan.Zero);
        var to     = new DateTimeOffset(today.AddDays(1), TimeSpan.Zero);
        
        var events = await _calendar.GetEventsAsync(from, to);

        return Ok(events);
    }

    [HttpGet("eventsbydate")]
    public async Task<IActionResult> GetEventsByDate( string date )
    {
        if (DateTimeOffset.TryParse(date, out var parsed)
                          .Not())
            return BadRequest($"I couldn't parse '{date}' as a date. Please use a format like 'YYYY-MM-DD'.");

        var day    = parsed.UtcDateTime.Date;
        var from   = new DateTimeOffset(day,            TimeSpan.Zero);
        var to     = new DateTimeOffset(day.AddDays(1), TimeSpan.Zero);
        var events = await _calendar.GetEventsAsync(from, to);

        var eventsList = CalendarActions.FormatEvents(events
                                                    , $"Calendar for {day:yyyy-MM-dd}");
        
        return events.Count == 0
                       ? Ok($"No events on your calendar for {day:yyyy-MM-dd}.")
                       : Ok(eventsList);
    }
}
