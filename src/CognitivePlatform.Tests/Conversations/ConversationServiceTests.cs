using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Domains.Conversations;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace CognitivePlatform.Tests.Conversations;

public class ConversationServiceTests
{
    private readonly Mock<IObjectStore>              _storeMock;
    private readonly Mock<ITranscriptionService>     _transcriptionMock;
    private readonly Mock<ISpeakerDiarizationService> _diarizationMock;
    private readonly ConversationService            _service;

    public ConversationServiceTests()
    {
        _storeMock          = new Mock<IObjectStore>();
        _transcriptionMock  = new Mock<ITranscriptionService>();
        _diarizationMock    = new Mock<ISpeakerDiarizationService>();
        _service            = new ConversationService( _storeMock.Object
                                                       , _transcriptionMock.Object
                                                       , _diarizationMock.Object
                                                       , NullLogger<ConversationService>.Instance );
    }

    [Fact]
    public async Task CreateRecordingAsync_AssignsGuid_WhenIdIsEmpty()
    {
        var record = new ConversationRecord { Id = Guid.Empty, Title = "Test Recording" };

        var result = await _service.CreateRecordingAsync(record);

        Assert.NotEqual(Guid.Empty, result.Id);
        _storeMock.Verify(s => s.Save(record, null, result.Id.ToString()), Times.Once);
    }

    [Fact]
    public async Task GetRecordingAsync_ReturnsNull_WhenSoftDeleted()
    {
        var id = Guid.NewGuid();
        var record = new ConversationRecord { Id = id, IsDeleted = true };

        _storeMock.Setup(s => s.GetAsync<ConversationRecord>(id.ToString(), null, default))
                  .ReturnsAsync(record);

        var result = await _service.GetRecordingAsync(id);

        Assert.Null(result);
    }

    [Fact]
    public async Task ProcessTranscriptionAsync_UpdatesRecordAndSavesTranscript()
    {
        var conversationId = Guid.NewGuid();
        var record = new ConversationRecord { Id = conversationId, Status = TranscriptionStatus.NotProcessed };
        using var audioStream = ConversationAudioGenerator.GenerateSyntheticWavStream(5.0);

        var expectedTranscript = new Transcript
        {
            ConversationId = conversationId
          , Status         = TranscriptionStatus.Completed
          , Segments       = new List<TranscriptSegment> { new() { Text = "Test segment" } }
        };

        _storeMock.Setup(s => s.GetAsync<ConversationRecord>(conversationId.ToString(), null, default))
                  .ReturnsAsync(record);

        _transcriptionMock.Setup(t => t.TranscribeAudioAsync(conversationId, audioStream, "audio/wav", default))
                          .ReturnsAsync(expectedTranscript);

        var result = await _service.ProcessTranscriptionAsync(conversationId, audioStream);

        Assert.NotNull(result);
        Assert.Equal(TranscriptionStatus.Completed, result.Status);
        Assert.Equal(TranscriptionStatus.Completed, record.Status);
        _storeMock.Verify(s => s.Save(expectedTranscript, null, $"transcript_{conversationId}"), Times.Once);
    }

    [Fact]
    public async Task DiarizeTranscriptAsync_InvokesDiarizationServiceAndPersists()
    {
        var conversationId = Guid.NewGuid();
        var initialTranscript = new Transcript { ConversationId = conversationId, IsDiarized = false };
        var diarizedTranscript = new Transcript { ConversationId = conversationId, IsDiarized = true };
        using var audioStream = ConversationAudioGenerator.GenerateSyntheticWavStream(5.0);

        _storeMock.Setup(s => s.GetAsync<Transcript>($"transcript_{conversationId}", null, default))
                  .ReturnsAsync(initialTranscript);

        _diarizationMock.Setup(d => d.DiarizeTranscriptAsync(initialTranscript, audioStream, default))
                        .ReturnsAsync(diarizedTranscript);

        var result = await _service.DiarizeTranscriptAsync(conversationId, audioStream);

        Assert.NotNull(result);
        Assert.True(result.IsDiarized);
        _storeMock.Verify(s => s.Save(diarizedTranscript, null, $"transcript_{conversationId}"), Times.Once);
    }

    [Fact]
    public async Task MapParticipantsAsync_UpdatesSpeakerLabels_AndPersistsParticipants()
    {
        var conversationId = Guid.NewGuid();
        var initialTranscript = new Transcript
        {
            ConversationId = conversationId
          , Segments       = new List<TranscriptSegment>
            {
                new() { SpeakerId = "Speaker 1", SpeakerLabel = "Speaker 1", Text = "Hello" },
                new() { SpeakerId = "Speaker 2", SpeakerLabel = "Speaker 2", Text = "Hi there" }
            }
        };

        _storeMock.Setup(s => s.GetAsync<Transcript>($"transcript_{conversationId}", null, default))
                  .ReturnsAsync(initialTranscript);

        var speakerMap = new Dictionary<string, string>
        {
            ["Speaker 1"] = "Ben"
          , ["Speaker 2"] = "Sarah"
        };

        var result = await _service.MapParticipantsAsync(conversationId, speakerMap);

        Assert.NotNull(result);
        Assert.Equal("Ben", result.Segments[0].SpeakerLabel);
        Assert.Equal("Speaker 1", result.Segments[0].SpeakerId); // Raw SpeakerId preserved
        Assert.Equal("Sarah", result.Segments[1].SpeakerLabel);
        Assert.Equal("Speaker 2", result.Segments[1].SpeakerId); // Raw SpeakerId preserved
        _storeMock.Verify(s => s.Save(initialTranscript, null, $"transcript_{conversationId}"), Times.Once);
    }
}
