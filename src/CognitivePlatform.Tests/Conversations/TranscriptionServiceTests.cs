using CognitivePlatform.Api.Domains.Conversations;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CognitivePlatform.Tests.Conversations;

public class TranscriptionServiceTests
{
    private readonly LocalAudioTranscriptionService _service;

    public TranscriptionServiceTests()
    {
        _service = new LocalAudioTranscriptionService(NullLogger<LocalAudioTranscriptionService>.Instance);
    }

    [Fact]
    public async Task TranscribeAudioAsync_WithSyntheticWav_ProducesTimestampedSegments()
    {
        var conversationId = Guid.NewGuid();
        using var audioStream = ConversationAudioGenerator.GenerateSyntheticWavStream(durationSeconds: 12.0);

        var transcript = await _service.TranscribeAudioAsync(conversationId, audioStream);

        Assert.NotNull(transcript);
        Assert.Equal(conversationId, transcript.ConversationId);
        Assert.Equal(TranscriptionStatus.Completed, transcript.Status);
        Assert.NotEmpty(transcript.Segments);
        Assert.Equal(3, transcript.Segments.Count);

        var firstSegment = transcript.Segments[0];
        Assert.Equal(TimeSpan.Zero, firstSegment.Start);
        Assert.Equal(TimeSpan.FromSeconds(5.0), firstSegment.End);
        Assert.NotEmpty(firstSegment.Text);
    }

    [Fact]
    public async Task TranscribeAudioAsync_WithEmptyStream_ReturnsFailedStatus()
    {
        var conversationId = Guid.NewGuid();
        using var emptyStream = new MemoryStream();

        var transcript = await _service.TranscribeAudioAsync(conversationId, emptyStream);

        Assert.NotNull(transcript);
        Assert.Equal(conversationId, transcript.ConversationId);
        Assert.Equal(TranscriptionStatus.Failed, transcript.Status);
        Assert.NotNull(transcript.ErrorMessage);
    }
}
