using CognitivePlatform.Api.Domains.Conversations;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CognitivePlatform.Tests.Conversations;

public class SpeakerDiarizationServiceTests
{
    private readonly LocalSpeakerDiarizationService _service;

    public SpeakerDiarizationServiceTests()
    {
        _service = new LocalSpeakerDiarizationService(NullLogger<LocalSpeakerDiarizationService>.Instance);
    }

    [Fact]
    public async Task DiarizeTranscriptAsync_AttributesTwoSpeakers_ToTranscriptSegments()
    {
        var conversationId = Guid.NewGuid();
        var transcript = new Transcript
        {
            ConversationId = conversationId
          , Status         = TranscriptionStatus.Completed
          , Segments       = new List<TranscriptSegment>
            {
                new() { Start = TimeSpan.FromSeconds(0), End = TimeSpan.FromSeconds(5), Text = "First speaker turn" }
              , new() { Start = TimeSpan.FromSeconds(5), End = TimeSpan.FromSeconds(10), Text = "Second speaker turn" }
              , new() { Start = TimeSpan.FromSeconds(10), End = TimeSpan.FromSeconds(15), Text = "First speaker turn again" }
            }
        };

        using var syntheticAudio = ConversationAudioGenerator.GenerateSyntheticWavStream(15.0);

        var result = await _service.DiarizeTranscriptAsync(transcript, syntheticAudio);

        Assert.NotNull(result);
        Assert.True(result.IsDiarized);
        Assert.NotNull(result.DiarizedAtUtc);
        Assert.Equal(3, result.Segments.Count);

        Assert.Equal("Speaker 1", result.Segments[0].SpeakerLabel);
        Assert.Equal("Speaker 2", result.Segments[1].SpeakerLabel);
        Assert.Equal("Speaker 1", result.Segments[2].SpeakerLabel);
    }

    [Fact]
    public async Task DiarizeTranscriptAsync_HandlesNullAudioStream_UsingTurnFallback()
    {
        var conversationId = Guid.NewGuid();
        var transcript = new Transcript
        {
            ConversationId = conversationId
          , Status         = TranscriptionStatus.Completed
          , Segments       = new List<TranscriptSegment>
            {
                new() { Start = TimeSpan.FromSeconds(0), End = TimeSpan.FromSeconds(5), Text = "Turn 1" }
              , new() { Start = TimeSpan.FromSeconds(5), End = TimeSpan.FromSeconds(10), Text = "Turn 2" }
            }
        };

        var result = await _service.DiarizeTranscriptAsync(transcript, audioStream: MemoryStream.Null);

        Assert.NotNull(result);
        Assert.True(result.IsDiarized);
        Assert.Equal("Speaker 1", result.Segments[0].SpeakerLabel);
        Assert.Equal("Speaker 2", result.Segments[1].SpeakerLabel);
    }
}
