using CognitivePlatform.Api.Data;
using CP.Shared.Primitives.Avails.Extensions;
using Microsoft.Extensions.Logging;

namespace CognitivePlatform.Api.Domains.Conversations;

public class ConversationService : IConversationService
{
    private readonly IObjectStore                 _objectStore;
    private readonly ITranscriptionService        _transcriptionService;
    private readonly ISpeakerDiarizationService   _diarizationService;
    private readonly ILogger<ConversationService> _logger;

    public ConversationService( IObjectStore objectStore
                              , ITranscriptionService transcriptionService
                              , ISpeakerDiarizationService diarizationService
                              , ILogger<ConversationService> logger )
    {
        _objectStore          = objectStore;
        _transcriptionService = transcriptionService;
        _diarizationService   = diarizationService;
        _logger               = logger;
    }

    public async Task<ConversationRecord> CreateRecordingAsync( ConversationRecord record, CancellationToken cancellationToken = default )
    {
        if (record.Id == Guid.Empty)
        {
            record.Id = Guid.NewGuid();
        }

        await _objectStore.Save(record, partitionKey: null, id: record.Id.ToString());
        return record;
    }

    public async Task<ConversationRecord?> GetRecordingAsync( Guid id, CancellationToken cancellationToken = default )
    {
        var record = await _objectStore.GetAsync<ConversationRecord>(id.ToString(), partitionKey: null, cancellationToken: cancellationToken);
        if (record != null && record.IsDeleted)
        {
            return null;
        }
        return record;
    }

    public async Task<List<ConversationRecord>> ListRecordingsAsync( CancellationToken cancellationToken = default )
    {
        var items = await _objectStore.ListAsync<ConversationRecord>(partitionKey: null, fromUtc: null, toUtc: null, cancellationToken: cancellationToken);
        return items.Where(record => !record.IsDeleted)
                    .OrderByDescending(record => record.RecordedAtUtc)
                    .ToList();
    }

    public async Task<bool> DeleteRecordingAsync( Guid id, CancellationToken cancellationToken = default )
    {
        var record = await GetRecordingAsync(id, cancellationToken);
        if (record == null)
        {
            return false;
        }

        record.IsDeleted = true;
        record.DeletedUtc = DateTime.UtcNow;
        await _objectStore.Save(record, partitionKey: null, id: record.Id.ToString());

        var transcript = await GetTranscriptAsync(id, cancellationToken);
        if (transcript != null)
        {
            transcript.IsDeleted = true;
            transcript.DeletedUtc = DateTime.UtcNow;
            await _objectStore.Save(transcript, partitionKey: null, id: $"transcript_{id}");
        }

        return true;
    }

    public async Task<Transcript> ProcessTranscriptionAsync( Guid conversationId
                                                          , Stream audioStream
                                                          , string mimeType = "audio/wav"
                                                          , CancellationToken cancellationToken = default )
    {
        var record = await GetRecordingAsync(conversationId, cancellationToken);
        if (record != null)
        {
            record.Status = TranscriptionStatus.Processing;
            await _objectStore.Save(record, partitionKey: null, id: record.Id.ToString());
        }

        var transcript = await _transcriptionService.TranscribeAudioAsync(conversationId, audioStream, mimeType, cancellationToken);

        await _objectStore.Save(transcript, partitionKey: null, id: $"transcript_{conversationId}");

        if (record != null)
        {
            record.Status = transcript.Status;
            await _objectStore.Save(record, partitionKey: null, id: record.Id.ToString());
        }

        return transcript;
    }

    public async Task<Transcript> DiarizeTranscriptAsync( Guid conversationId
                                                        , Stream audioStream
                                                        , CancellationToken cancellationToken = default )
    {
        var transcript = await GetTranscriptAsync(conversationId, cancellationToken);
        if (transcript == null)
        {
            return new Transcript
            {
                ConversationId = conversationId
              , Status         = TranscriptionStatus.Failed
              , ErrorMessage   = "Transcript not found for diarization."
            };
        }

        var diarized = await _diarizationService.DiarizeTranscriptAsync(transcript, audioStream, cancellationToken);
        await _objectStore.Save(diarized, partitionKey: null, id: $"transcript_{conversationId}");
        return diarized;
    }

    public async Task<Transcript?> GetTranscriptAsync( Guid conversationId, CancellationToken cancellationToken = default )
    {
        var transcript = await _objectStore.GetAsync<Transcript>($"transcript_{conversationId}", partitionKey: null, cancellationToken: cancellationToken);
        if (transcript != null && transcript.IsDeleted)
        {
            return null;
        }
        return transcript;
    }
}
