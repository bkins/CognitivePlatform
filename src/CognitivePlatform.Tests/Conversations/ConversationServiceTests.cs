using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Domains.Conversations;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace CognitivePlatform.Tests.Conversations;

public class ConversationServiceTests
{
    private readonly Mock<IObjectStore>           _storeMock;
    private readonly Mock<ITranscriptionService>  _transcriptionMock;
    private readonly ConversationService         _service;

    public ConversationServiceTests()
    {
        _storeMock          = new Mock<IObjectStore>();
        _transcriptionMock  = new Mock<ITranscriptionService>();
        _service            = new ConversationService( _storeMock.Object
                                                       , _transcriptionMock.Object
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
}
