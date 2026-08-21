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
    private readonly IConversationOrchestrator  _orchestrator;
    private readonly ITelemetrySink             _telemetry;
    private readonly IConversationTurnStore     _turnStore;
    private readonly IConversationMetadataStore _metadataStore;

    public ConversationController( IConversationOrchestrator  orchestrator
                                 , ITelemetrySink             telemetry
                                 , TelemetryContext           telemetryContext
                                 , IConversationTurnStore     turnStore
                                 , IConversationMetadataStore metadataStore )
    {
        _orchestrator  = orchestrator;
        _telemetry     = telemetry;
        _turnStore     = turnStore;
        _metadataStore = metadataStore;
    }

    [HttpPost("converse")]
    public async Task<ActionResult<ConverseResponse>> Converse([FromBody] ConverseRequest request)
    {
        if (request.FastPath)
        {
            request.Streaming = false;
        }

        var result = await _orchestrator.ConverseAsync(request);

        if (result.Success.Not() && !result.IsVaultUnlockRequired && !result.IsVaultSetupRequired) return BadRequest(result);
        
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
        })
        .TakeLast(last);

        return Ok(dtos);
    }

    [HttpGet]
    public async Task<IActionResult> ListConversations()
    {
        var conversations = await _metadataStore.ListAllAsync();
        return Ok(conversations);
    }

    [HttpGet("sync")]
    public async Task<IActionResult> SyncConversations([FromQuery] string? workspace, [FromQuery] DateTimeOffset? since)
    {
        var cutoff = since ?? DateTimeOffset.MinValue;
        var allMetas = await _metadataStore.ListAllAsync();

        var filteredMetas = allMetas
            .Where(meta => !meta.IsDeleted && (meta.LastActiveUtc >= cutoff.UtcDateTime || meta.CreatedUtc >= cutoff.UtcDateTime))
            .OrderByDescending(meta => meta.LastActiveUtc)
            .ToList();

        var updatedConversations = new List<ConversationSyncDto>();

        foreach (var meta in filteredMetas)
        {
            var turns = _turnStore.GetRecent(meta.ConversationId, 50)
                                  .Where(turn => turn.OccurredAt >= cutoff)
                                  .ToList();

            var messages = new List<ConversationSyncMessageDto>();
            foreach (var turn in turns)
            {
                if (turn.UserMessage.HasValue())
                {
                    messages.Add(new ConversationSyncMessageDto
                    {
                        Sender       = "user"
                      , Content      = turn.UserMessage
                      , TimestampUtc = turn.OccurredAt
                    });
                }

                if (turn.AssistantMessage.HasValue())
                {
                    var reasoning = turn.Path == TurnPath.FastPath
                                        ? "Fast-Path (Direct rule-based execution; no LLM reasoning required)"
                                        : turn.ActionName.HasValue()
                                            ? $"[Plan] Selected action '{turn.ActionName}' -> Executed successfully."
                                            : "Standard Completion (Direct response generation; no Chain-of-Thought reasoning emitted)";

                    messages.Add(new ConversationSyncMessageDto
                    {
                        Sender           = "assistant"
                      , Content          = turn.AssistantMessage
                      , ActionName       = turn.ActionName
                      , WasFastPath      = turn.Path == TurnPath.FastPath
                      , ReasoningContent = reasoning
                      , TimestampUtc     = turn.OccurredAt
                    });
                }
            }

            updatedConversations.Add(new ConversationSyncDto
            {
                Id           = meta.ConversationId
              , Title        = meta.Name ?? meta.ConversationId
              , LastUpdated  = new DateTimeOffset(meta.LastActiveUtc, TimeSpan.Zero)
              , MessageCount = meta.MessageCount
              , Messages     = messages
            });
        }

        var response = new SyncResponseDto
        {
            Workspace            = workspace.HasValue() ? workspace! : "Default"
          , SyncedAtUtc          = DateTimeOffset.UtcNow
          , UpdatedConversations = updatedConversations
        };

        return Ok(response);
    }

    [HttpGet("{id}/metadata")]
    public async Task<IActionResult> GetMetadata([FromRoute] string id)
    {
        var metadata = await _metadataStore.GetAsync(id);
        if (metadata is null)
            return NotFound();

        return Ok(metadata);
    }

    [HttpPut("{id}/name")]
    public async Task<IActionResult> RenameConversation( [FromRoute] string                  id
                                                        , [FromBody]  RenameConversationRequest request )
    {
        var metadata = await _metadataStore.GetAsync(id);
        if (metadata is null)
            return NotFound();

        metadata.Name = request.Name;
        await _metadataStore.UpsertAsync(metadata);

        return Ok(metadata);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteConversation([FromRoute] string id)
    {
        var metadata = await _metadataStore.GetAsync(id);
        if (metadata is null)
            return NotFound();

        await _metadataStore.SoftDeleteAsync(id);

        return NoContent();
    }
}