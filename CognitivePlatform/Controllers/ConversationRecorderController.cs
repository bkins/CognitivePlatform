using CognitivePlatform.Api.Domains.Conversations;
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
}
