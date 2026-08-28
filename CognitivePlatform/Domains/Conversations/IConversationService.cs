namespace CognitivePlatform.Api.Domains.Conversations;

public interface IConversationService
{
    Task<ConversationRecord> CreateRecordingAsync( ConversationRecord record, CancellationToken cancellationToken = default );
    Task<ConversationRecord?> GetRecordingAsync( Guid id, CancellationToken cancellationToken = default );
    Task<List<ConversationRecord>> ListRecordingsAsync( CancellationToken cancellationToken = default );
    Task<bool> DeleteRecordingAsync( Guid id, CancellationToken cancellationToken = default );
    Task<Transcript> ProcessTranscriptionAsync( Guid conversationId, Stream audioStream, string mimeType = "audio/wav", CancellationToken cancellationToken = default );
    Task<Transcript> DiarizeTranscriptAsync( Guid conversationId, Stream audioStream, CancellationToken cancellationToken = default );
    Task<Transcript?> GetTranscriptAsync( Guid conversationId, CancellationToken cancellationToken = default );
    Task<Transcript?> MapParticipantsAsync( Guid conversationId, Dictionary<string, string> speakerMap, CancellationToken cancellationToken = default );
    Task<List<ConversationParticipant>> GetParticipantsAsync( Guid conversationId, CancellationToken cancellationToken = default );
    Task<ConversationDetails?> GetConversationDetailsAsync( Guid conversationId, CancellationToken cancellationToken = default );
    Task<List<ConversationRecord>> SearchConversationsAsync( string? query = null, string? participantName = null, DateTimeOffset? fromDate = null, DateTimeOffset? toDate = null, CancellationToken cancellationToken = default );
    Task<bool> SaveAudioAsync( Guid conversationId, Stream audioStream, string mimeType = "audio/wav", CancellationToken cancellationToken = default );
    Task<(Stream? Stream, string ContentType)> GetAudioAsync( Guid conversationId, CancellationToken cancellationToken = default );
    Task<ConversationAnalysis> AnalyzeConversationAsync( Guid conversationId, CancellationToken cancellationToken = default );
    Task<ConversationAnalysis?> GetAnalysisAsync( Guid conversationId, CancellationToken cancellationToken = default );
}
