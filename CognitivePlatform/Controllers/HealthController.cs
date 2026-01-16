using CognitivePlatform.Api.Contracts;
using CognitivePlatform.Api.Conversation;
using CognitivePlatform.Api.Orchestrator;
using Microsoft.AspNetCore.Mvc;
using CognitivePlatform.Api.Avails;
using CognitivePlatform.Api.Telemetry;
using Microsoft.AspNetCore.Mvc;

namespace CognitivePlatform.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    private readonly ITelemetrySink _telemetry;
    
    public HealthController(ITelemetrySink telemetrySink)
    {
        _telemetry = telemetrySink;
    }
    
    [HttpGet("ping")]
    public IActionResult Get()
    {
        _telemetry.Track("HealthController_Get: Ping ->", "Pong");
        
        return Ok(new { Pong = "Pong" });
    }
}