using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Domains.Conversations;
using CognitivePlatform.Api.Domains.Knowledge;
using CognitivePlatform.Api.Domains.Personas.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace CognitivePlatform.Tests.Conversations;

public class ConversationServiceTests
{
    private readonly Mock<IObjectStore>                 _storeMock;
    private readonly Mock<ITranscriptionService>        _transcriptionMock;
    private readonly Mock<ISpeakerDiarizationService>    _diarizationMock;
    private readonly Mock<IConversationAnalyzer>        _analyzerMock;
    private readonly Mock<IConversationMemoryExtractor> _memoryExtractorMock;
    private readonly Mock<IKnowledgeIngestionService>   _knowledgeIngestionMock;
    private readonly ConversationService               _service;

    public ConversationServiceTests()
    {
        _storeMock              = new Mock<IObjectStore>();
        _transcriptionMock      = new Mock<ITranscriptionService>();
        _diarizationMock        = new Mock<ISpeakerDiarizationService>();
        _analyzerMock           = new Mock<IConversationAnalyzer>();
        _memoryExtractorMock    = new Mock<IConversationMemoryExtractor>();
        _knowledgeIngestionMock = new Mock<IKnowledgeIngestionService>();

        _service = new ConversationService( _storeMock.Object
                                           , _transcriptionMock.Object
                                           , _diarizationMock.Object
                                           , _analyzerMock.Object
                                           , NullLogger<ConversationService>.Instance
                                           , _memoryExtractorMock.Object
                                           , _knowledgeIngestionMock.Object );
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

        _transcriptionMock.Setup(t => t.TranscribeAudioAsync(conversationId, It.IsAny<Stream>(), "audio/wav", default))
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

    [Fact]
    public async Task GetConversationDetailsAsync_ReturnsCompleteAggregate_WhenRecordExists()
    {
        var conversationId = Guid.NewGuid();
        var record       = new ConversationRecord { Id = conversationId, Title = "Architecture Meeting" };
        var transcript   = new Transcript { ConversationId = conversationId, Segments = new List<TranscriptSegment> { new() { Text = "Discussing API" } } };
        var participants = new List<ConversationParticipant> { new() { ConversationId = conversationId, SpeakerId = "Speaker 1", DisplayName = "Ben" } };

        _storeMock.Setup(s => s.GetAsync<ConversationRecord>(conversationId.ToString(), null, default))
                  .ReturnsAsync(record);
        _storeMock.Setup(s => s.GetAsync<Transcript>($"transcript_{conversationId}", null, default))
                  .ReturnsAsync(transcript);
        _storeMock.Setup(s => s.ListAsync<ConversationParticipant>(null, null, null, default))
                  .ReturnsAsync(participants);

        var details = await _service.GetConversationDetailsAsync(conversationId);

        Assert.NotNull(details);
        Assert.Equal(record, details.Record);
        Assert.Equal(transcript, details.Transcript);
        Assert.Single(details.Participants);
        Assert.Equal("Ben", details.Participants[0].DisplayName);
    }

    [Fact]
    public async Task SearchConversationsAsync_FiltersByQueryAndParticipant_Correctly()
    {
        var conversationId1 = Guid.NewGuid();
        var conversationId2 = Guid.NewGuid();

        var record1 = new ConversationRecord { Id = conversationId1, Title = "Sprint Planning", RecordedAtUtc = DateTime.UtcNow.AddHours(-2) };
        var record2 = new ConversationRecord { Id = conversationId2, Title = "Budget Review", RecordedAtUtc = DateTime.UtcNow.AddHours(-1) };

        _storeMock.Setup(s => s.ListAsync<ConversationRecord>(null, null, null, default))
                  .ReturnsAsync(new List<ConversationRecord> { record1, record2 });

        var transcript1 = new Transcript
        {
            ConversationId = conversationId1
          , Segments       = new List<TranscriptSegment> { new() { Text = "We need to plan sprint tasks", SpeakerLabel = "Ben" } }
        };
        var transcript2 = new Transcript
        {
            ConversationId = conversationId2
          , Segments       = new List<TranscriptSegment> { new() { Text = "Reviewing expenses", SpeakerLabel = "Sarah" } }
        };

        _storeMock.Setup(s => s.GetAsync<Transcript>($"transcript_{conversationId1}", null, default)).ReturnsAsync(transcript1);
        _storeMock.Setup(s => s.GetAsync<Transcript>($"transcript_{conversationId2}", null, default)).ReturnsAsync(transcript2);

        var participants1 = new List<ConversationParticipant> { new() { ConversationId = conversationId1, SpeakerId = "Speaker 1", DisplayName = "Ben" } };
        var participants2 = new List<ConversationParticipant> { new() { ConversationId = conversationId2, SpeakerId = "Speaker 1", DisplayName = "Sarah" } };

        _storeMock.Setup(s => s.ListAsync<ConversationParticipant>(null, null, null, default))
                  .ReturnsAsync(new List<ConversationParticipant> { participants1[0], participants2[0] });

        var queryResults = await _service.SearchConversationsAsync(query: "sprint", participantName: null, fromDate: null, toDate: null);
        Assert.Single(queryResults);
        Assert.Equal(conversationId1, queryResults[0].Id);

        var participantResults = await _service.SearchConversationsAsync(query: null, participantName: "Sarah", fromDate: null, toDate: null);
        Assert.Single(participantResults);
        Assert.Equal(conversationId2, participantResults[0].Id);
    }

    [Fact]
    public async Task SaveAudioAsync_PersistsWavFileToDisk_AndUpdatesRecord()
    {
        var conversationId = Guid.NewGuid();
        using var audioStream = ConversationAudioGenerator.GenerateSyntheticWavStream(3.0);

        var result = await _service.SaveAudioAsync(conversationId, audioStream, "audio/wav");

        Assert.True(result);
        _storeMock.Verify(s => s.Save(It.Is<ConversationRecord>(r => r.Id == conversationId && !string.IsNullOrEmpty(r.AudioFilePath)), null, conversationId.ToString()), Times.Once);

        var (stream, contentType) = await _service.GetAudioAsync(conversationId);
        Assert.NotNull(stream);
        Assert.Equal("audio/wav", contentType);
        stream.Dispose();
    }

    [Fact]
    public async Task AnalyzeConversationAsync_RunsAnalyzerAndSavesResult_WhenTranscriptExists()
    {
        var conversationId = Guid.NewGuid();
        var record         = new ConversationRecord { Id = conversationId, Title = "Design Review" };
        var transcript     = new Transcript
        {
            ConversationId = conversationId,
            Segments       = new List<TranscriptSegment> { new() { Text = "Let's review the API." } }
        };
        var expectedAnalysis = new ConversationAnalysis
        {
            ConversationId = conversationId,
            Summary        = "Reviewed API design.",
            Status         = AnalysisStatus.Completed
        };

        _storeMock.Setup(s => s.GetAsync<ConversationRecord>(conversationId.ToString(), null, default))
                  .ReturnsAsync(record);
        _storeMock.Setup(s => s.GetAsync<Transcript>($"transcript_{conversationId}", null, default))
                  .ReturnsAsync(transcript);
        _storeMock.Setup(s => s.ListAsync<ConversationParticipant>(null, null, null, default))
                  .ReturnsAsync(new List<ConversationParticipant>());
        _storeMock.Setup(s => s.GetAsync<ConversationAnalysis>($"analysis_{conversationId}", null, default))
                  .ReturnsAsync((ConversationAnalysis?)null);

        _analyzerMock.Setup(a => a.AnalyzeAsync(It.IsAny<ConversationDetails>(), default))
                     .ReturnsAsync(expectedAnalysis);

        var result = await _service.AnalyzeConversationAsync(conversationId);

        Assert.NotNull(result);
        Assert.Equal(AnalysisStatus.Completed, result.Status);
        Assert.Equal("Reviewed API design.", result.Summary);
        _storeMock.Verify(s => s.Save(expectedAnalysis, null, $"analysis_{conversationId}"), Times.Once);
    }

    [Fact]
    public async Task AnalyzeConversationAsync_ReturnsFailed_WhenTranscriptMissing()
    {
        var conversationId = Guid.NewGuid();
        var record         = new ConversationRecord { Id = conversationId, Title = "No Audio" };

        _storeMock.Setup(s => s.GetAsync<ConversationRecord>(conversationId.ToString(), null, default))
                  .ReturnsAsync(record);
        _storeMock.Setup(s => s.GetAsync<Transcript>($"transcript_{conversationId}", null, default))
                  .ReturnsAsync((Transcript?)null);

        var result = await _service.AnalyzeConversationAsync(conversationId);

        Assert.NotNull(result);
        Assert.Equal(AnalysisStatus.Failed, result.Status);
        Assert.Contains("No transcript available", result.ErrorMessage);
    }

    [Fact]
    public async Task GetAnalysisAsync_ReturnsNull_WhenSoftDeleted()
    {
        var conversationId = Guid.NewGuid();
        var analysis       = new ConversationAnalysis { ConversationId = conversationId, IsDeleted = true };

        _storeMock.Setup(s => s.GetAsync<ConversationAnalysis>($"analysis_{conversationId}", null, default))
                  .ReturnsAsync(analysis);

        var result = await _service.GetAnalysisAsync(conversationId);

        Assert.Null(result);
    }

    [Fact]
    public async Task DeleteRecordingAsync_SoftDeletesAnalysis_WhenPresent()
    {
        var conversationId = Guid.NewGuid();
        var record         = new ConversationRecord { Id = conversationId };
        var transcript     = new Transcript { ConversationId = conversationId };
        var analysis       = new ConversationAnalysis { ConversationId = conversationId, Status = AnalysisStatus.Completed };

        _storeMock.Setup(s => s.GetAsync<ConversationRecord>(conversationId.ToString(), null, default))
                  .ReturnsAsync(record);
        _storeMock.Setup(s => s.GetAsync<Transcript>($"transcript_{conversationId}", null, default))
                  .ReturnsAsync(transcript);
        _storeMock.Setup(s => s.GetAsync<ConversationAnalysis>($"analysis_{conversationId}", null, default))
                  .ReturnsAsync(analysis);

        var result = await _service.DeleteRecordingAsync(conversationId);

        Assert.True(result);
        Assert.True(analysis.IsDeleted);
        _storeMock.Verify(s => s.Save(analysis, null, $"analysis_{conversationId}"), Times.Once);
    }

    [Fact]
    public async Task ExtractMemoriesAsync_ExtractsAndSavesMemories_WhenTranscriptExists()
    {
        var conversationId = Guid.NewGuid();
        var record         = new ConversationRecord { Id = conversationId, Title = "Tech Sync" };
        var transcript     = new Transcript
        {
            ConversationId = conversationId,
            Segments       = new List<TranscriptSegment> { new() { Text = "Sarah got a new role." } }
        };
        var expectedMemories = new List<ConversationMemoryCandidate>
        {
            new()
            {
                Id             = Guid.NewGuid()
              , ConversationId = conversationId
              , Category       = "Fact"
              , Content        = "Sarah got a new role as Engineering Lead."
            }
        };

        _storeMock.Setup(s => s.GetAsync<ConversationRecord>(conversationId.ToString(), null, default))
                  .ReturnsAsync(record);
        _storeMock.Setup(s => s.GetAsync<Transcript>($"transcript_{conversationId}", null, default))
                  .ReturnsAsync(transcript);
        _storeMock.Setup(s => s.ListAsync<ConversationParticipant>(null, null, null, default))
                  .ReturnsAsync(new List<ConversationParticipant>());
        _storeMock.Setup(s => s.GetAsync<ConversationAnalysis>($"analysis_{conversationId}", null, default))
                  .ReturnsAsync((ConversationAnalysis?)null);

        _memoryExtractorMock.Setup(e => e.ExtractMemoriesAsync(It.IsAny<ConversationDetails>(), default))
                            .ReturnsAsync(expectedMemories);

        var result = await _service.ExtractMemoriesAsync(conversationId);

        Assert.Single(result);
        Assert.Equal("Fact", result[0].Category);
        _storeMock.Verify(s => s.Save(expectedMemories, null, $"memories_{conversationId}"), Times.Once);
    }

    [Fact]
    public async Task ConfirmMemoryAsync_UpdatesStateAndCreatesPersonaMemoryWithProvenance()
    {
        var conversationId = Guid.NewGuid();
        var memoryId       = Guid.NewGuid();
        var segmentId      = Guid.NewGuid();

        var candidate = new ConversationMemoryCandidate
        {
            Id                         = memoryId
          , ConversationId             = conversationId
          , Category                   = "Commitment"
          , Content                    = "Ben will design SQLite schema."
          , Speaker                    = "Ben"
          , SourceTranscriptSegmentIds = new List<Guid> { segmentId }
          , State                      = MemoryState.Provisional
        };

        _storeMock.Setup(s => s.GetAsync<List<ConversationMemoryCandidate>>($"memories_{conversationId}", null, default))
                  .ReturnsAsync(new List<ConversationMemoryCandidate> { candidate });

        var personaMemory = await _service.ConfirmMemoryAsync(conversationId, memoryId);

        Assert.NotNull(personaMemory);
        Assert.Equal(MemoryState.Canonical, candidate.State);
        Assert.Equal(MemoryState.Canonical, personaMemory.State);
        Assert.Equal(MemorySource.ConversationRecollection, personaMemory.Source);
        Assert.Contains(segmentId.ToString(), personaMemory.InferenceChain);
        _storeMock.Verify(s => s.Save(It.IsAny<PersonaMemory>(), null, It.Is<string>(id => id.StartsWith("persona_memory_"))), Times.Once);
    }

    [Fact]
    public async Task QueryMemoriesAsync_ReturnsMatchingMemoriesAcrossConversations()
    {
        var conversation1Id = Guid.NewGuid();
        var conversation2Id = Guid.NewGuid();

        var records = new List<ConversationRecord>
        {
            new() { Id = conversation1Id, Title = "Meeting 1", RecordedAtUtc = DateTime.UtcNow }
          , new() { Id = conversation2Id, Title = "Meeting 2", RecordedAtUtc = DateTime.UtcNow }
        };

        var memories1 = new List<ConversationMemoryCandidate>
        {
            new() { Id = Guid.NewGuid(), ConversationId = conversation1Id, Category = "Fact", Content = "Sarah joined the team." }
        };

        var memories2 = new List<ConversationMemoryCandidate>
        {
            new() { Id = Guid.NewGuid(), ConversationId = conversation2Id, Category = "Decision", Content = "Migrate to SQLite." }
        };

        _storeMock.Setup(s => s.ListAsync<ConversationRecord>(null, null, null, default))
                  .ReturnsAsync(records);
        _storeMock.Setup(s => s.GetAsync<List<ConversationMemoryCandidate>>($"memories_{conversation1Id}", null, default))
                  .ReturnsAsync(memories1);
        _storeMock.Setup(s => s.GetAsync<List<ConversationMemoryCandidate>>($"memories_{conversation2Id}", null, default))
                  .ReturnsAsync(memories2);

        var sarahResults = await _service.QueryMemoriesAsync("Sarah");
        Assert.Single(sarahResults);
        Assert.Equal("Sarah joined the team.", sarahResults[0].Content);

        var sqliteResults = await _service.QueryMemoriesAsync("SQLite");
        Assert.Single(sqliteResults);
        Assert.Equal("Migrate to SQLite.", sqliteResults[0].Content);
    }
}

