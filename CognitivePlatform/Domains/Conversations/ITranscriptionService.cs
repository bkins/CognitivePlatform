namespace CognitivePlatform.Api.Domains.Conversations;

public interface ITranscriptionService
{
    Task<Transcript> TranscribeAudioAsync( Guid conversationId
                                        , Stream audioStream
                                        , string mimeType = "audio/wav"
                                        , CancellationToken cancellationToken = default );
}
