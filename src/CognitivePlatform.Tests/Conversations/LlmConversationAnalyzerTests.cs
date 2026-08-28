using System.Text.Json;
using CognitivePlatform.Api.Domains.Conversations;
using CognitivePlatform.Api.Interpreter;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace CognitivePlatform.Tests.Conversations;

public class LlmConversationAnalyzerTests
{
    private readonly Mock<ILlmClientFactory>                 _factoryMock;
    private readonly Mock<ILlmClient>                        _llmClientMock;
    private readonly LlmConversationAnalyzer                 _analyzer;

    public LlmConversationAnalyzerTests()
    {
        _factoryMock   = new Mock<ILlmClientFactory>();
        _llmClientMock = new Mock<ILlmClient>();

        _factoryMock.Setup(f => f.Create()).Returns(_llmClientMock.Object);
        _factoryMock.Setup(f => f.DefaultProvider).Returns(LlmProvider.Groq);

        _analyzer = new LlmConversationAnalyzer(
            _factoryMock.Object,
            NullLogger<LlmConversationAnalyzer>.Instance);
    }

    [Fact]
    public async Task AnalyzeAsync_ReturnsFailedStatus_WhenTranscriptIsNull()
    {
        var details = new ConversationDetails
        {
            Record     = new ConversationRecord { Id = Guid.NewGuid(), Title = "Empty Meeting" },
            Transcript = null
        };

        var result = await _analyzer.AnalyzeAsync(details);

        Assert.Equal(AnalysisStatus.Failed, result.Status);
        Assert.Contains("No transcript segments", result.ErrorMessage);
    }

    [Fact]
    public async Task AnalyzeAsync_ReturnsFailedStatus_WhenSegmentsEmpty()
    {
        var details = new ConversationDetails
        {
            Record     = new ConversationRecord { Id = Guid.NewGuid(), Title = "Empty Meeting" },
            Transcript = new Transcript { ConversationId = Guid.NewGuid(), Segments = new List<TranscriptSegment>() }
        };

        var result = await _analyzer.AnalyzeAsync(details);

        Assert.Equal(AnalysisStatus.Failed, result.Status);
        Assert.Contains("No transcript segments", result.ErrorMessage);
    }

    [Fact]
    public async Task AnalyzeAsync_ParsesStructuredResponseAndMapsProvenance_WhenLlmReturnsValidJson()
    {
        var conversationId = Guid.NewGuid();
        var segment1Id     = Guid.NewGuid();
        var segment2Id     = Guid.NewGuid();

        var details = new ConversationDetails
        {
            Record     = new ConversationRecord { Id = conversationId, Title = "Architecture Review" },
            Transcript = new Transcript
            {
                ConversationId = conversationId,
                Segments = new List<TranscriptSegment>
                {
                    new() { Id = segment1Id, SpeakerLabel = "Speaker 1", SpeakerName = "Alice", Text = "Should we migrate to SQLite?" },
                    new() { Id = segment2Id, SpeakerLabel = "Speaker 2", SpeakerName = "Bob", Text = "Yes, let's migrate and I will write the schema." }
                }
            },
            Participants = new List<ConversationParticipant>
            {
                new() { ConversationId = conversationId, SpeakerLabel = "Speaker 1", DisplayName = "Alice" },
                new() { ConversationId = conversationId, SpeakerLabel = "Speaker 2", DisplayName = "Bob" }
            }
        };

        var llmJson = """
        {
          "summary": "Alice and Bob agreed to migrate to SQLite, and Bob took the action item to write the schema.",
          "topics": [
            { "content": "Database migration", "segmentIndices": [0, 1] }
          ],
          "questions": [
            { "content": "Should we migrate to SQLite?", "segmentIndices": [0] }
          ],
          "decisions": [
            { "content": "Migrate database to SQLite", "segmentIndices": [1] }
          ],
          "actionItems": [
            { "content": "Write SQLite schema", "segmentIndices": [1] }
          ],
          "importantStatements": [
            { "content": "Bob volunteered for schema design", "segmentIndices": [1] }
          ]
        }
        """;

        _llmClientMock.Setup(c => c.SendAsync(It.IsAny<string>(), null, default))
                      .ReturnsAsync(new LlmResponse { Content = llmJson });

        var result = await _analyzer.AnalyzeAsync(details);

        Assert.Equal(AnalysisStatus.Completed, result.Status);
        Assert.Equal(conversationId, result.ConversationId);
        Assert.Equal("Alice and Bob agreed to migrate to SQLite, and Bob took the action item to write the schema.", result.Summary);

        Assert.Single(result.Topics);
        Assert.Equal("Database migration", result.Topics[0].Content);
        Assert.Equal(2, result.Topics[0].SourceTranscriptSegmentIds.Count);
        Assert.Contains(segment1Id, result.Topics[0].SourceTranscriptSegmentIds);
        Assert.Contains(segment2Id, result.Topics[0].SourceTranscriptSegmentIds);

        Assert.Single(result.Questions);
        Assert.Equal("Should we migrate to SQLite?", result.Questions[0].Content);
        Assert.Equal(segment1Id, result.Questions[0].SourceTranscriptSegmentIds[0]);

        Assert.Single(result.Decisions);
        Assert.Equal("Migrate database to SQLite", result.Decisions[0].Content);
        Assert.Equal(segment2Id, result.Decisions[0].SourceTranscriptSegmentIds[0]);

        Assert.Single(result.ActionItems);
        Assert.Equal("Write SQLite schema", result.ActionItems[0].Content);
        Assert.Equal(segment2Id, result.ActionItems[0].SourceTranscriptSegmentIds[0]);

        Assert.Single(result.ImportantStatements);
        Assert.Equal("Bob volunteered for schema design", result.ImportantStatements[0].Content);
    }

    [Fact]
    public async Task AnalyzeAsync_HandlesMarkdownFencedJson_Gracefully()
    {
        var conversationId = Guid.NewGuid();
        var segmentId      = Guid.NewGuid();

        var details = new ConversationDetails
        {
            Record     = new ConversationRecord { Id = conversationId, Title = "Quick Sync" },
            Transcript = new Transcript
            {
                ConversationId = conversationId,
                Segments = new List<TranscriptSegment>
                {
                    new() { Id = segmentId, SpeakerLabel = "Speaker 1", Text = "Everything is deployed." }
                }
            }
        };

        var markdownJson = """
        ```json
        {
          "summary": "Deployment is complete.",
          "topics": [],
          "questions": [],
          "decisions": [],
          "actionItems": [],
          "importantStatements": []
        }
        ```
        """;

        _llmClientMock.Setup(c => c.SendAsync(It.IsAny<string>(), null, default))
                      .ReturnsAsync(new LlmResponse { Content = markdownJson });

        var result = await _analyzer.AnalyzeAsync(details);

        Assert.Equal(AnalysisStatus.Completed, result.Status);
        Assert.Equal("Deployment is complete.", result.Summary);
    }

    [Fact]
    public async Task AnalyzeAsync_ReturnsFailedStatus_WhenLlmThrowsException()
    {
        var conversationId = Guid.NewGuid();
        var details = new ConversationDetails
        {
            Record     = new ConversationRecord { Id = conversationId, Title = "Sync" },
            Transcript = new Transcript
            {
                ConversationId = conversationId,
                Segments = new List<TranscriptSegment> { new() { Text = "Test" } }
            }
        };

        _llmClientMock.Setup(c => c.SendAsync(It.IsAny<string>(), null, default))
                      .ThrowsAsync(new HttpRequestException("LLM connection timed out"));

        var result = await _analyzer.AnalyzeAsync(details);

        Assert.Equal(AnalysisStatus.Failed, result.Status);
        Assert.Contains("LLM connection timed out", result.ErrorMessage);
    }
}
