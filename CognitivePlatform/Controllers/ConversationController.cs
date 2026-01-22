    using System.Diagnostics;
    using CognitivePlatform.Api.Contracts;
    using CognitivePlatform.Api.Conversation;
    using CognitivePlatform.Api.Orchestrator;
    using CognitivePlatform.Api.Telemetry;
    using Microsoft.AspNetCore.Mvc;

namespace CognitivePlatform.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ConversationController : ControllerBase
{
    private readonly IConversationOrchestrator _orchestrator;
    private readonly ITelemetrySink            _telemetry;

    public ConversationController(IConversationOrchestrator orchestrator, ITelemetrySink telemetry)
    {
        _orchestrator = orchestrator;
        _telemetry    = telemetry;
    }

    [HttpPost("converse")]
    public async Task<ActionResult<ConverseResponse>> Converse([FromBody] ConverseRequest request)
    {
        if (request.FastPath)
        {
            request.Streaming = false;
        }

        var sw = new Stopwatch();
        sw.Start();
        _telemetry.Track("Converse.Start", $"Input: {request.Input ?? "Not `request.Input` provided."}");
        
        var result = await _orchestrator.ConverseAsync(request);
        sw.Stop();
        var ts                     = sw.Elapsed;
        var totalMinutesAndSeconds = $"{(int)ts.TotalMinutes}:{ts.Seconds:D2}";          // Seconds within the current minute
        
        result.Debug += $"{Environment.NewLine}{totalMinutesAndSeconds}";
        
        _telemetry.Track("Converse.End", totalMinutesAndSeconds);
        
        return Ok(result);
    }

    [HttpPost("converse/stream")]
    public async Task StreamConverse ([FromBody] ConverseRequest request
                                    , CancellationToken          ct)
    {
        var sw = new Stopwatch();
        sw.Start();
        _telemetry.Track("StreamConverse.Start", request.Input ?? "Not `request.Input` provided.");

        Response.Headers.Append("Content-Type",      "text/event-stream");
        Response.Headers.Append("Cache-Control",     "no-cache");
        Response.Headers.Append("X-Accel-Buffering", "no");

        await foreach (var chunk in _orchestrator.StreamAsync(request, ct))
        {
            await Response.WriteAsync($"data: {chunk}\n\n", ct);
            await Response.Body.FlushAsync(ct);
        }
        
        sw.Stop();
        _telemetry.Track("StreamConverse.End", (sw.ElapsedMilliseconds/1000).ToString());

    }
}