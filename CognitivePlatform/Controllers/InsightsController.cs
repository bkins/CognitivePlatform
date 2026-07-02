using CognitivePlatform.Api.Insights;
using CognitivePlatform.Api.Insights.Models;
using Microsoft.AspNetCore.Mvc;

namespace CognitivePlatform.Api.Controllers;

[ApiController]
[Route("api/insights")]
public sealed class InsightsController : ControllerBase
{
    private readonly INotificationEngine _notificationEngine;

    public InsightsController(INotificationEngine notificationEngine)
    {
        _notificationEngine = notificationEngine ?? throw new ArgumentNullException(nameof(notificationEngine));
    }

    /// <summary>
    /// Triggers ambient/background evaluation of all registered insight providers.
    /// Deduplication and throttling are applied; generated insights are persisted.
    /// </summary>
    [HttpPost("evaluate")]
    public async Task<ActionResult<IReadOnlyList<Insight>>> Evaluate( [FromQuery] string?      sessionId
                                                                   , CancellationToken ct = default )
    {
        var insights = await _notificationEngine.EvaluateAsync(sessionId, DateTime.UtcNow, ct);
        return Ok(insights);
    }
}
