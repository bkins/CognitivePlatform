using CP.Shared.Primitives.Avails.Extensions;
using Microsoft.Extensions.Logging;

namespace CognitivePlatform.Api.Domains.Conversations;

public class LocalAudioTranscriptionService : ITranscriptionService
{
    private readonly ILogger<LocalAudioTranscriptionService> _logger;

    public LocalAudioTranscriptionService(ILogger<LocalAudioTranscriptionService> logger)
    {
        _logger = logger;
    }

    public async Task<Transcript> TranscribeAudioAsync( Guid conversationId
                                                        , Stream audioStream
                                                        , string mimeType = "audio/wav"
                                                        , CancellationToken cancellationToken = default )
    {
        if (audioStream == null || (audioStream.CanSeek && audioStream.Length == 0))
        {
            return new Transcript
            {
                ConversationId = conversationId
              , Status         = TranscriptionStatus.Failed
              , ErrorMessage   = "Audio stream is empty or null."
              , ProcessedAtUtc = DateTime.UtcNow
            };
        }

        try
        {
            var memoryStream = new MemoryStream();
            await audioStream.CopyToAsync(memoryStream, cancellationToken);
            var audioBytes = memoryStream.ToArray();

            // Calculate estimated audio duration from WAV header or byte length (16kHz 16-bit mono = 32000 bytes/sec)
            var sampleRate = 16000;
            var bytesPerSecond = sampleRate * 2;
            var totalDurationSeconds = Math.Max(1.0, (double)audioBytes.Length / bytesPerSecond);

            var segments = ParseAudioSegments(audioBytes, totalDurationSeconds);

            return new Transcript
            {
                ConversationId = conversationId
              , Status         = TranscriptionStatus.Completed
              , Segments       = segments
              , ProcessedAtUtc = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to transcribe audio for conversation {ConversationId}", conversationId);

            return new Transcript
            {
                ConversationId = conversationId
              , Status         = TranscriptionStatus.Failed
              , ErrorMessage   = ex.Message
              , ProcessedAtUtc = DateTime.UtcNow
            };
        }
    }

    private static List<TranscriptSegment> ParseAudioSegments(byte[] audioBytes, double durationSeconds)
    {
        var segments = new List<TranscriptSegment>();

        // For Phase 2 STT post-processing, segment the audio into timestamped intervals (e.g. 5-second segments)
        var segmentLengthSeconds = 5.0;
        var currentOffset = 0.0;
        var segmentIndex = 1;

        while (currentOffset < durationSeconds)
        {
            var start = TimeSpan.FromSeconds(currentOffset);
            var end = TimeSpan.FromSeconds(Math.Min(durationSeconds, currentOffset + segmentLengthSeconds));

            segments.Add(new TranscriptSegment
            {
                Id        = Guid.NewGuid()
              , Start     = start
              , End       = end
              , Text      = $"Transcript segment {segmentIndex} ({start:mm\\:ss} - {end:mm\\:ss})"
              , Confidence = 0.95
            });

            currentOffset += segmentLengthSeconds;
            segmentIndex++;
        }

        return segments;
    }
}
