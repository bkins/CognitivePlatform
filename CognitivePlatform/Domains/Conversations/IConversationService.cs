namespace CognitivePlatform.Api.Domains.Conversations;

public interface IConversationService
{
    Task<ConversationRecord> CreateRecordingAsync( ConversationRecord record, CancellationToken cancellationToken = default );
    Task<ConversationRecord?> GetRecordingAsync( Guid id, CancellationToken cancellationToken = default );
    Task<List<ConversationRecord>> ListRecordingsAsync( CancellationToken cancellationToken = default );
    Task<bool> DeleteRecordingAsync( Guid id, CancellationToken cancellationToken = default );
    Task<Transcript> ProcessTranscriptionAsync( Guid conversationId, Stream audioStream, string mimeType = "audio/wav", CancellationToken cancellationToken = default );
    Task<Transcript?> GetTranscriptAsync( Guid conversationId, CancellationToken cancellationToken = default );
}
