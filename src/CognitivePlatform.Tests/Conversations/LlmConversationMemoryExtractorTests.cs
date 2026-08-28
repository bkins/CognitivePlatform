using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using CognitivePlatform.Api.Domains.Conversations;
using CognitivePlatform.Api.Domains.Personas.Models;
using CognitivePlatform.Api.Interpreter;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace CognitivePlatform.Tests.Conversations;

public class LlmConversationMemoryExtractorTests
{
    private readonly Mock<ILlmClientFactory>               _factoryMock;
    private readonly Mock<ILlmClient>                      _llmClientMock;
    private readonly LlmConversationMemoryExtractor        _extractor;

    public LlmConversationMemoryExtractorTests()
    {
        _factoryMock   = new Mock<ILlmClientFactory>();
        _llmClientMock = new Mock<ILlmClient>();

        _factoryMock.Setup(f => f.Create()).Returns(_llmClientMock.Object);

        _extractor = new LlmConversationMemoryExtractor(
            _factoryMock.Object,
            NullLogger<LlmConversationMemoryExtractor>.Instance);
    }

    [Fact]
    public async Task ExtractMemoriesAsync_ReturnsEmptyList_WhenSegmentsNullOrEmpty()
    {
        var details = new ConversationDetails
        {
            Record     = new ConversationRecord { Id = Guid.NewGuid(), Title = "Empty" },
            Transcript = null
        };

        var results = await _extractor.ExtractMemoriesAsync(details);

        Assert.Empty(results);
    }

    [Fact]
    public async Task ExtractMemoriesAsync_ParsesJsonArrayAndMapsSegmentIds_WhenLlmResponds()
    {
        var conversationId = Guid.NewGuid();
        var segment1Id     = Guid.NewGuid();
        var segment2Id     = Guid.NewGuid();

        var details = new ConversationDetails
        {
            Record     = new ConversationRecord { Id = conversationId, Title = "Tech Sync" },
            Transcript = new Transcript
            {
                ConversationId = conversationId,
                Segments = new List<TranscriptSegment>
                {
                    new() { Id = segment1Id, SpeakerLabel = "Speaker 1", SpeakerName = "Sarah", Text = "I started my new role as Engineering Lead on Monday." },
                    new() { Id = segment2Id, SpeakerLabel = "Speaker 2", SpeakerName = "Ben", Text = "Congratulations! I will send over the architecture docs by tomorrow." }
                }
            },
            Participants = new List<ConversationParticipant>
            {
                new() { SpeakerLabel = "Speaker 1", DisplayName = "Sarah" },
                new() { SpeakerLabel = "Speaker 2", DisplayName = "Ben" }
            }
        };

        var llmJson = """
        [
          {
            "category": "Fact",
            "content": "Sarah started her new role as Engineering Lead on Monday.",
            "speaker": "Sarah",
            "segmentIndices": [0],
            "confidence": 0.98
          },
          {
            "category": "Commitment",
            "content": "Ben will send over the architecture docs by tomorrow.",
            "speaker": "Ben",
            "segmentIndices": [1],
            "confidence": 0.95
          }
        ]
        """;

        _llmClientMock.Setup(client => client.SendAsync(It.IsAny<string>(), null, default))
                      .ReturnsAsync(new LlmResponse { Content = llmJson });

        var results = await _extractor.ExtractMemoriesAsync(details);

        Assert.Equal(2, results.Count);

        var first = results[0];
        Assert.Equal(conversationId, first.ConversationId);
        Assert.Equal("Fact", first.Category);
        Assert.Equal("Sarah started her new role as Engineering Lead on Monday.", first.Content);
        Assert.Equal("Sarah", first.Speaker);
        Assert.Equal(MemoryState.Provisional, first.State);
        Assert.Single(first.SourceTranscriptSegmentIds);
        Assert.Equal(segment1Id, first.SourceTranscriptSegmentIds[0]);

        var second = results[1];
        Assert.Equal("Commitment", second.Category);
        Assert.Equal("Ben will send over the architecture docs by tomorrow.", second.Content);
        Assert.Equal("Ben", second.Speaker);
        Assert.Single(second.SourceTranscriptSegmentIds);
        Assert.Equal(segment2Id, second.SourceTranscriptSegmentIds[0]);
    }

    [Fact]
    public async Task ExtractMemoriesAsync_DerivesFromAnalysis_WhenLlmThrowsException()
    {
        var conversationId = Guid.NewGuid();
        var segmentId      = Guid.NewGuid();

        var details = new ConversationDetails
        {
            Record     = new ConversationRecord { Id = conversationId, Title = "Offline Sync" },
            Transcript = new Transcript
            {
                ConversationId = conversationId,
                Segments = new List<TranscriptSegment>
                {
                    new() { Id = segmentId, Text = "We decided to proceed with the release." }
                }
            },
            Analysis = new ConversationAnalysis
            {
                ConversationId = conversationId,
                Decisions      = new List<AnalysisDerivedItem>
                {
                    new() { Content = "Proceed with release.", SourceTranscriptSegmentIds = new List<Guid> { segmentId } }
                },
                ActionItems = new List<AnalysisDerivedItem>
                {
                    new() { Content = "Tag v1.0 milestone.", SourceTranscriptSegmentIds = new List<Guid> { segmentId } }
                }
            }
        };

        _llmClientMock.Setup(client => client.SendAsync(It.IsAny<string>(), null, default))
                      .ThrowsAsync(new HttpRequestException("LLM offline"));

        var results = await _extractor.ExtractMemoriesAsync(details);

        Assert.Equal(2, results.Count);
        Assert.Contains(results, r => r.Category == "Decision" && r.Content == "Proceed with release.");
        Assert.Contains(results, r => r.Category == "Commitment" && r.Content == "Tag v1.0 milestone.");
    }
}
