using CognitivePlatform.Api.Telemetry;
using Microsoft.AspNetCore.Mvc;

namespace CognitivePlatform.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SystemController : ControllerBase
{
    private readonly ITelemetrySink _telemetry;
    
    public SystemController(ITelemetrySink telemetrySink)
    {
        _telemetry = telemetrySink;
    }
    
    [HttpGet("environment")]
    public IActionResult Get()
    {
        _telemetry.Track("SystemController.Get.Started", "Environment");
        
        return Ok(new { Pong = "Pong" });
    }
}