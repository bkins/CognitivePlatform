    using System.Diagnostics;
    using CognitivePlatform.Api.Contracts;
    using CognitivePlatform.Api.Conversation;
    using CognitivePlatform.Api.Orchestrator;
    using CognitivePlatform.Api.Telemetry;
    using CognitivePlatform.Api.Telemetry.Events;
    using CP.Shared.Primitives.Avails.Extensions;
    using Microsoft.AspNetCore.Mvc;

namespace CognitivePlatform.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ConversationController : ControllerBase
{
    private readonly IConversationOrchestrator _orchestrator;
    private readonly ITelemetrySink            _telemetry;
    private readonly IConversationTurnStore    _turnStore;

    public ConversationController( IConversationOrchestrator orchestrator
                                 , ITelemetrySink            telemetry
                                 , TelemetryContext          telemetryContext
                                 , IConversationTurnStore    turnStore )
    {
        _orchestrator     = orchestrator;
        _telemetry        = telemetry;
        _turnStore        = turnStore;
    }

    [HttpPost("converse")]
    public async Task<ActionResult<ConverseResponse>> Converse([FromBody] ConverseRequest request)
    {
        if (request.FastPath)
        {
            request.Streaming = false;
        }

        var result = await _orchestrator.ConverseAsync(request);

        if (result.Success.Not()) return BadRequest(result);
        
        return Ok(result);
    }

    [HttpPost("converse/stream")]
    public async Task StreamConverse ([FromBody] ConverseRequest request
                                    , CancellationToken          ct)
    {
        
        Response.Headers.Append("Content-Type",      "text/event-stream");
        Response.Headers.Append("Cache-Control",     "no-cache");
        Response.Headers.Append("X-Accel-Buffering", "no");

        await foreach (var chunk in _orchestrator.StreamAsync(request, ct))
        {
            await Response.WriteAsync($"data: {chunk}\n\n", ct);
            await Response.Body.FlushAsync(ct);
        }

    }

    [HttpGet("{id}/history")]
    public IActionResult GetHistory([FromRoute] string id, [FromQuery] int last = 20)
    {
        if (last <= 0)
            last = 20;

        var turns = _turnStore.GetRecent(id, last);

        var dtos = turns.SelectMany(turn => new[]
        {
            new ConversationTurnDto { Role = "user",      Content = turn.UserMessage,      Timestamp = turn.OccurredAt }
          , new ConversationTurnDto { Role = "assistant", Content = turn.AssistantMessage, Timestamp = turn.OccurredAt }
        });

        return Ok(dtos);
    }
}