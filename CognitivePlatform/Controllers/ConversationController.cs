    using CognitivePlatform.Api.Contracts;
    using CognitivePlatform.Api.Conversation;
    using CognitivePlatform.Api.Orchestrator;
    using Microsoft.AspNetCore.Mvc;

namespace CognitivePlatform.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ConversationController : ControllerBase
{
    private readonly IConversationOrchestrator _orchestrator;

    public ConversationController(IConversationOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
    }

    [HttpPost("converse")]
    public async Task<ActionResult<ConverseResponse>> Converse([FromBody] ConverseRequest request)
    {
        if (request.FastPath)
        {
            request.Streaming = false;
        }
        
        var result = await _orchestrator.ConverseAsync(request);
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
}