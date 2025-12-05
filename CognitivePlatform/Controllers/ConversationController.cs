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
        var result = await _orchestrator.ConverseAsync(request);
        return Ok(result);
    }
}