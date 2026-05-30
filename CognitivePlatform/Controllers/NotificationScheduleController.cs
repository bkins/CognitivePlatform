using CognitivePlatform.Api.Integrations.Notifications;
using Microsoft.AspNetCore.Mvc;

namespace CognitivePlatform.Api.Controllers;

[ApiController]
[Route("api/notifications")]
public sealed class NotificationScheduleController : ControllerBase
{
    private readonly INotificationScheduleProvider _provider;

    public NotificationScheduleController(INotificationScheduleProvider provider)
    {
        _provider = provider;
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
}
