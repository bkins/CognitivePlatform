using CognitivePlatform.Api.Domains.Conversations;
using CognitivePlatform.Api.Domains.Personas.Models;
using Microsoft.AspNetCore.Mvc;

namespace CognitivePlatform.Api.Controllers;

[ApiController]
[Route("api/recorder/conversations")]
public class ConversationRecorderController : ControllerBase
{
    private readonly IConversationService _conversationService;

    public ConversationRecorderController(IConversationService conversationService)
    {
        _conversationService = conversationService;
    }

    [HttpPost]
    public async Task<ActionResult<ConversationRecord>> CreateRecording( [FromBody] ConversationRecord record
                                                                      , CancellationToken cancellationToken )
    {
        var created = await _conversationService.CreateRecordingAsync(record, cancellationToken);
        return Ok(created);
    }

    [HttpGet]
    public async Task<ActionResult<List<ConversationRecord>>> ListRecordings(CancellationToken cancellationToken)
    {
        var recordings = await _conversationService.ListRecordingsAsync(cancellationToken);
        return Ok(recordings);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ConversationRecord>> GetRecording( [FromRoute] Guid id
                                                                    , CancellationToken cancellationToken )
    {
        var recording = await _conversationService.GetRecordingAsync(id, cancellationToken);
        if (recording is null)
        {
            return NotFound();
        }
        return Ok(recording);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteRecording( [FromRoute] Guid id
                                                     , CancellationToken cancellationToken )
    {
        var deleted = await _conversationService.DeleteRecordingAsync(id, cancellationToken);
        if (!deleted)
        {
            return NotFound();
        }
        return NoContent();
    }

    [HttpPost("{id:guid}/transcribe")]
    public async Task<ActionResult<Transcript>> TranscribeRecording( [FromRoute] Guid id
                                                                    , CancellationToken cancellationToken )
    {
        Stream audioStream;
        if (Request.HasFormContentType && Request.Form.Files.Count > 0)
        {
            var file = Request.Form.Files[0];
            audioStream = file.OpenReadStream();
        }
        else if (Request.Body != null && Request.Body.CanRead)
        {
            audioStream = Request.Body;
        }
        else
        {
            return BadRequest("Audio content must be provided as a form file upload or raw binary stream.");
        }

        var transcript = await _conversationService.ProcessTranscriptionAsync(id, audioStream, "audio/wav", cancellationToken);
        return Ok(transcript);
    }

    [HttpPost("{id:guid}/diarize")]
    public async Task<ActionResult<Transcript>> DiarizeRecording( [FromRoute] Guid id
                                                                  , CancellationToken cancellationToken )
    {
        Stream audioStream = MemoryStream.Null;
        if (Request.HasFormContentType && Request.Form.Files.Count > 0)
        {
            audioStream = Request.Form.Files[0].OpenReadStream();
        }
        else if (Request.Body != null && Request.Body.CanRead)
        {
            audioStream = Request.Body;
        }

        var transcript = await _conversationService.DiarizeTranscriptAsync(id, audioStream, cancellationToken);
        return Ok(transcript);
    }

    [HttpGet("{id:guid}/transcript")]
    public async Task<ActionResult<Transcript>> GetTranscript( [FromRoute] Guid id
                                                             , CancellationToken cancellationToken )
    {
        var transcript = await _conversationService.GetTranscriptAsync(id, cancellationToken);
        if (transcript is null)
        {
            return NotFound();
        }
        return Ok(transcript);
    }

    [HttpPost("{id:guid}/participants")]
    public async Task<ActionResult<Transcript>> MapParticipants( [FromRoute] Guid id
                                                                , [FromBody] Dictionary<string, string> speakerMap
                                                                , CancellationToken cancellationToken )
    {
        var transcript = await _conversationService.MapParticipantsAsync(id, speakerMap, cancellationToken);
        if (transcript is null)
        {
            return NotFound();
        }
        return Ok(transcript);
    }

    [HttpGet("{id:guid}/participants")]
    public async Task<ActionResult<List<ConversationParticipant>>> GetParticipants( [FromRoute] Guid id
                                                                                    , CancellationToken cancellationToken )
    {
        var participants = await _conversationService.GetParticipantsAsync(id, cancellationToken);
        return Ok(participants);
    }

    [HttpGet("{id:guid}/details")]
    public async Task<ActionResult<ConversationDetails>> GetConversationDetails( [FromRoute] Guid id
                                                                                , CancellationToken cancellationToken )
    {
        var details = await _conversationService.GetConversationDetailsAsync(id, cancellationToken);
        if (details is null)
        {
            return NotFound();
        }
        return Ok(details);
    }

    [HttpPost("{id:guid}/audio")]
    public async Task<IActionResult> UploadAudio( [FromRoute] Guid id
                                                , CancellationToken cancellationToken )
    {
        Stream audioStream;
        string mimeType = "audio/wav";

        if (Request.HasFormContentType && Request.Form.Files.Count > 0)
        {
            var file = Request.Form.Files[0];
            audioStream = file.OpenReadStream();
            mimeType = string.IsNullOrWhiteSpace(file.ContentType) ? "audio/wav" : file.ContentType;
        }
        else if (Request.Body != null && Request.Body.CanRead)
        {
            audioStream = Request.Body;
            if (!string.IsNullOrWhiteSpace(Request.ContentType))
            {
                mimeType = Request.ContentType;
            }
        }
        else
        {
            return BadRequest("Audio content must be provided as a form file upload or raw binary stream.");
        }

        var saved = await _conversationService.SaveAudioAsync(id, audioStream, mimeType, cancellationToken);
        if (!saved)
        {
            return BadRequest("Failed to save audio file stream.");
        }
        return Ok(new { conversationId = id, status = "Saved" });
    }

    [HttpGet("{id:guid}/audio")]
    public async Task<IActionResult> GetAudio( [FromRoute] Guid id
                                             , CancellationToken cancellationToken )
    {
        var (stream, contentType) = await _conversationService.GetAudioAsync(id, cancellationToken);
        if (stream is null)
        {
            return NotFound();
        }
        return File(stream, contentType, enableRangeProcessing: true);
    }

    [HttpGet("search")]
    public async Task<ActionResult<List<ConversationRecord>>> SearchRecordings( [FromQuery] string? q
                                                                              , [FromQuery] string? participant
                                                                              , [FromQuery] DateTimeOffset? from
                                                                              , [FromQuery] DateTimeOffset? to
                                                                              , CancellationToken cancellationToken )
    {
        var results = await _conversationService.SearchConversationsAsync(q, participant, from, to, cancellationToken);
        return Ok(results);
    }

    [HttpPost("{id:guid}/analyze")]
    public async Task<ActionResult<ConversationAnalysis>> AnalyzeConversation( [FromRoute] Guid id
                                                                             , CancellationToken cancellationToken )
    {
        var analysis = await _conversationService.AnalyzeConversationAsync(id, cancellationToken);
        return Ok(analysis);
    }

    [HttpGet("{id:guid}/analysis")]
    public async Task<ActionResult<ConversationAnalysis>> GetAnalysis( [FromRoute] Guid id
                                                                     , CancellationToken cancellationToken )
    {
        var analysis = await _conversationService.GetAnalysisAsync(id, cancellationToken);
        if (analysis is null)
        {
            return NotFound();
        }
        return Ok(analysis);
    }

    [HttpPost("{id:guid}/memories/extract")]
    public async Task<ActionResult<List<ConversationMemoryCandidate>>> ExtractMemories( [FromRoute] Guid id
                                                                                      , CancellationToken cancellationToken )
    {
        var memories = await _conversationService.ExtractMemoriesAsync(id, cancellationToken);
        return Ok(memories);
    }

    [HttpGet("{id:guid}/memories")]
    public async Task<ActionResult<List<ConversationMemoryCandidate>>> GetMemories( [FromRoute] Guid id
                                                                                  , CancellationToken cancellationToken )
    {
        var memories = await _conversationService.GetMemoriesAsync(id, cancellationToken);
        return Ok(memories);
    }

    [HttpPost("{id:guid}/memories/{memoryId:guid}/confirm")]
    public async Task<ActionResult<PersonaMemory>> ConfirmMemory( [FromRoute] Guid id
                                                                , [FromRoute] Guid memoryId
                                                                , CancellationToken cancellationToken )
    {
        var confirmed = await _conversationService.ConfirmMemoryAsync(id, memoryId, cancellationToken);
        if (confirmed is null)
        {
            return NotFound();
        }
        return Ok(confirmed);
    }

    [HttpGet("memories/query")]
    public async Task<ActionResult<List<ConversationMemoryCandidate>>> QueryMemories( [FromQuery] string? q
                                                                                    , CancellationToken cancellationToken )
    {
        var results = await _conversationService.QueryMemoriesAsync(q ?? string.Empty, cancellationToken);
        return Ok(results);
    }
}
