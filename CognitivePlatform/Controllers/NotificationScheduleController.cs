using CognitivePlatform.Api.Integrations.Notifications;
using Microsoft.AspNetCore.Mvc;

namespace CognitivePlatform.Api.Controllers;

[ApiController]
[Route("api/notifications")]
public sealed class NotificationScheduleController : ControllerBase
{
    private readonly INotificationScheduleProvider _provider;
    private readonly INotificationPatternService   _patternService;

    public NotificationScheduleController( INotificationScheduleProvider provider
                                         , INotificationPatternService   patternService )
    {
        _provider       = provider;
        _patternService = patternService;
    }

    /// <summary>
    /// Returns the upcoming notification schedule evaluated from the given time.
    /// Satisfied conditions (day already open, task already complete, etc.) are excluded.
    /// Guard rules (max-per-day, min-gap, quiet hours) are applied before returning.
    /// </summary>
    [HttpGet("schedule")]
    public async Task<ActionResult<NotificationSchedule>> GetSchedule( [FromQuery] DateTimeOffset? from
                                                                      , CancellationToken          ct = default )
    {
        var fromTime = from ?? DateTimeOffset.Now;
        var schedule = await _provider.GetScheduleAsync(fromTime, ct);
        return Ok(schedule);
    }

    /// <summary>
    /// Records user interaction with a notification. Used to learn engagement patterns
    /// for adaptive scheduling. Valid actions: tapped, dismissed, acted.
    /// </summary>
    [HttpPost("feedback")]
    public async Task<IActionResult> PostFeedback( [FromBody] NotificationFeedbackRequest request
                                                 , CancellationToken                      ct = default )
    {
        var feedback = request.Action.ToLowerInvariant() switch
        {
            "tapped"    => (NotificationFeedback?)NotificationFeedback.Tapped
          , "dismissed" => NotificationFeedback.Dismissed
          , "acted"     => NotificationFeedback.ActedUpon
          , _           => null
        };

        if (feedback is null)
            return BadRequest($"Unknown action '{request.Action}'. Valid values: tapped, dismissed, acted.");

        await _patternService.RecordFeedbackAsync(request.ExternalId, feedback.Value, ct);
        return NoContent();
    }
}
