namespace CognitivePlatform.Api.Domains.Conversations;

public interface ISpeakerDiarizationService
{
    Task<Transcript> DiarizeTranscriptAsync( Transcript transcript
                                            , Stream audioStream
                                            , CancellationToken cancellationToken = default );
}
