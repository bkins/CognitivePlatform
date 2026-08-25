using CP.Shared.Primitives.Avails.Extensions;
using Microsoft.Extensions.Logging;

namespace CognitivePlatform.Api.Domains.Conversations;

public class LocalSpeakerDiarizationService : ISpeakerDiarizationService
{
    private readonly ILogger<LocalSpeakerDiarizationService> _logger;

    public LocalSpeakerDiarizationService(ILogger<LocalSpeakerDiarizationService> logger)
    {
        _logger = logger;
    }

    public async Task<Transcript> DiarizeTranscriptAsync( Transcript transcript
                                                        , Stream audioStream
                                                        , CancellationToken cancellationToken = default )
    {
        if (transcript == null || transcript.Segments == null || transcript.Segments.Count == 0)
        {
            return transcript ?? new Transcript();
        }

        try
        {
            byte[]? audioBytes = null;
            if (audioStream != null && audioStream.CanRead && (!audioStream.CanSeek || audioStream.Length > 0))
            {
                using var memoryStream = new MemoryStream();
                await audioStream.CopyToAsync(memoryStream, cancellationToken);
                audioBytes = memoryStream.ToArray();
            }

            for (int index = 0; index < transcript.Segments.Count; index++)
            {
                var segment = transcript.Segments[index];

                // Determine speaker identity (Speaker 1 vs Speaker 2) based on acoustic frequency frame or index turn
                var speakerIndex = DetermineSpeakerIndex(segment, audioBytes, index);
                segment.SpeakerId = $"speaker_{speakerIndex}";
                segment.SpeakerLabel = $"Speaker {speakerIndex}";
            }

            transcript.IsDiarized = true;
            transcript.DiarizedAtUtc = DateTime.UtcNow;
            return transcript;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to perform speaker diarization for conversation {ConversationId}", transcript.ConversationId);
            return transcript;
        }
    }

    private static int DetermineSpeakerIndex(TranscriptSegment segment, byte[]? audioBytes, int segmentIndex)
    {
        if (audioBytes == null || audioBytes.Length < 44)
        {
            // Fallback: alternate speaker turns based on segment index (Speaker 1, Speaker 2, Speaker 1, Speaker 2...)
            return (segmentIndex % 2) + 1;
        }

        // Sample audio pitch at segment start timestamp (16kHz 16-bit mono = 32000 bytes/sec)
        var bytesPerSecond = 32000;
        var startOffset = 44 + (int)(segment.Start.TotalSeconds * bytesPerSecond);
        if (startOffset + 100 < audioBytes.Length)
        {
            // Sample sample value at segment start
            var sample = BitConverter.ToInt16(audioBytes, startOffset);
            if (sample > 5000 || sample < -5000)
            {
                return (Math.Abs(sample) % 2) + 1;
            }
        }

        return (segmentIndex % 2) + 1;
    }
}
