using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Domains.Conversations;
using CognitivePlatform.Api.Domains.Conversations.Copilot;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace CognitivePlatform.Tests.Conversations;

public class CopilotServiceTests
{
    private readonly Mock<ITranscriptionService> _transcriptionServiceMock = new();
    private readonly Mock<IObjectStore>          _objectStoreMock          = new();
    private readonly Mock<IConversationService>  _conversationServiceMock  = new();
    private readonly CopilotService              _service;

    public CopilotServiceTests()
    {
        _service = new CopilotService(
            _transcriptionServiceMock.Object,
            _objectStoreMock.Object,
            _conversationServiceMock.Object,
            NullLogger<CopilotService>.Instance);
    }

    [Fact]
    public async Task ProcessSliceAsync_WithQuestionTrigger_RecallsMemoryAndReturnsInsight()
    {
        var conversationId = Guid.NewGuid();
        using var stream = new MemoryStream(new byte[100]);
        var request = new CopilotSliceRequest
                      {
                          SliceIndex      = 1
                        , OffsetSeconds   = 15.0
                        , DurationSeconds = 15.0
                      };

        var transcript = new Transcript
                         {
                             ConversationId = conversationId
                           , Status         = TranscriptionStatus.Completed
                           , Segments       = new List<TranscriptSegment>
                                              {
                                                  new() { Text = "Do you remember Sarah's dog's name?" }
                                              }
                         };

        var candidateMemories = new List<ConversationMemoryCandidate>
                                {
                                    new()
                                    {
                                        Id       = Guid.NewGuid()
                                      , Category = "Fact"
                                      , Content  = "Sarah's dog is named Milo, a golden retriever."
                                    }
                                };

        _transcriptionServiceMock
            .Setup(ts => ts.TranscribeAudioAsync(conversationId, It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(transcript);

        _conversationServiceMock
            .Setup(cs => cs.QueryMemoriesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(candidateMemories);

        _conversationServiceMock
            .Setup(cs => cs.GetParticipantsAsync(conversationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ConversationParticipant>());

        _objectStoreMock
            .Setup(os => os.GetAsync<List<CopilotInsight>>($"copilot_insights_{conversationId}", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CopilotInsight>());

        var result = await _service.ProcessSliceAsync(conversationId, stream, request);

        Assert.NotNull(result);
        Assert.Equal(conversationId, result.ConversationId);
        Assert.True(result.HasActionableInsight);
        Assert.Single(result.Insights);

        var insight = result.Insights[0];
        Assert.Equal(CopilotInsightType.RecallHint, insight.InsightType);
        Assert.Contains("Memory Recall", insight.Headline);
        Assert.Equal("Sarah's dog is named Milo, a golden retriever.", insight.Detail);
    }

    [Fact]
    public async Task ProcessSliceAsync_WithCommitmentTrigger_ReturnsCommitmentNotice()
    {
        var conversationId = Guid.NewGuid();
        using var stream = new MemoryStream(new byte[100]);
        var request = new CopilotSliceRequest
                      {
                          SliceIndex      = 2
                        , OffsetSeconds   = 30.0
                        , DurationSeconds = 15.0
                      };

        var transcript = new Transcript
                         {
                             ConversationId = conversationId
                           , Status         = TranscriptionStatus.Completed
                           , Segments       = new List<TranscriptSegment>
                                              {
                                                  new() { Text = "I will send the finalized project proposal by Friday." }
                                              }
                         };

        _transcriptionServiceMock
            .Setup(ts => ts.TranscribeAudioAsync(conversationId, It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(transcript);

        _conversationServiceMock
            .Setup(cs => cs.QueryMemoriesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ConversationMemoryCandidate>());

        _conversationServiceMock
            .Setup(cs => cs.GetParticipantsAsync(conversationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ConversationParticipant>());

        _objectStoreMock
            .Setup(os => os.GetAsync<List<CopilotInsight>>($"copilot_insights_{conversationId}", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CopilotInsight>());

        var result = await _service.ProcessSliceAsync(conversationId, stream, request);

        Assert.NotNull(result);
        Assert.True(result.HasActionableInsight);
        Assert.Single(result.Insights);

        var insight = result.Insights[0];
        Assert.Equal(CopilotInsightType.CommitmentNotice, insight.InsightType);
        Assert.Equal("Commitment Detected", insight.Headline);
        Assert.Contains("I will send the finalized project proposal", insight.Detail);
    }

    [Fact]
    public async Task ProcessSliceAsync_WithNoTriggers_ReturnsEmptyInsights()
    {
        var conversationId = Guid.NewGuid();
        using var stream = new MemoryStream(new byte[100]);
        var request = new CopilotSliceRequest { SliceIndex = 0 };

        var transcript = new Transcript
                         {
                             ConversationId = conversationId
                           , Status         = TranscriptionStatus.Completed
                           , Segments       = new List<TranscriptSegment>
                                              {
                                                  new() { Text = "Nice weather we have today." }
                                              }
                         };

        _transcriptionServiceMock
            .Setup(ts => ts.TranscribeAudioAsync(conversationId, It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(transcript);

        _conversationServiceMock
            .Setup(cs => cs.GetParticipantsAsync(conversationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ConversationParticipant>());

        var result = await _service.ProcessSliceAsync(conversationId, stream, request);

        Assert.NotNull(result);
        Assert.False(result.HasActionableInsight);
        Assert.Empty(result.Insights);
    }

    [Fact]
    public async Task DismissInsightAsync_MarksInsightAsDismissed_WhenInsightExists()
    {
        var conversationId = Guid.NewGuid();
        var insightId = Guid.NewGuid();
        var insights = new List<CopilotInsight>
                       {
                           new()
                           {
                               Id             = insightId
                             , ConversationId = conversationId
                             , Headline       = "Test Headline"
                             , Detail         = "Test Detail"
                             , IsDismissed    = false
                           }
                       };

        _objectStoreMock
            .Setup(os => os.GetAsync<List<CopilotInsight>>($"copilot_insights_{conversationId}", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(insights);

        var dismissed = await _service.DismissInsightAsync(conversationId, insightId);

        Assert.True(dismissed);
        Assert.True(insights[0].IsDismissed);
        _objectStoreMock.Verify(os => os.Save(insights, null, $"copilot_insights_{conversationId}"), Times.Once);
    }

    [Fact]
    public async Task GetInsightsAsync_ReturnsPersistedInsights()
    {
        var conversationId = Guid.NewGuid();
        var insights = new List<CopilotInsight>
                       {
                           new()
                           {
                               Id             = Guid.NewGuid()
                             , ConversationId = conversationId
                             , Headline       = "Insight 1"
                             , Detail         = "Detail 1"
                           },
                           new()
                           {
                               Id             = Guid.NewGuid()
                             , ConversationId = conversationId
                             , Headline       = "Insight 2"
                             , Detail         = "Detail 2"
                           }
                       };

        _objectStoreMock
            .Setup(os => os.GetAsync<List<CopilotInsight>>($"copilot_insights_{conversationId}", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(insights);

        var result = await _service.GetInsightsAsync(conversationId);

        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        Assert.Equal("Insight 1", result[0].Headline);
    }

    [Fact]
    public async Task ProcessLiveStreamChunkAsync_WithAudioChunk_ProducesLiveSegmentAndTalkTime()
    {
        var conversationId = Guid.NewGuid();
        using var stream = new MemoryStream(new byte[100]);
        var request = new LiveStreamChunkRequest
                      {
                          ChunkIndex      = 0
                        , OffsetSeconds   = 0.0
                        , DurationSeconds = 2.5
                      };

        var transcript = new Transcript
                         {
                             ConversationId = conversationId
                           , Status         = TranscriptionStatus.Completed
                           , Segments       = new List<TranscriptSegment>
                                              {
                                                  new() { Text = "Hello team, let's start the review." }
                                              }
                         };

        _transcriptionServiceMock
            .Setup(ts => ts.TranscribeAudioAsync(conversationId, It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(transcript);

        _objectStoreMock
            .Setup(os => os.GetAsync<Dictionary<string, double>>($"copilot_talktime_{conversationId}", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, double>());

        var result = await _service.ProcessLiveStreamChunkAsync(conversationId, stream, request);

        Assert.NotNull(result);
        Assert.Equal(conversationId, result.ConversationId);
        Assert.Equal(0, result.ChunkIndex);
        Assert.NotNull(result.Segment);
        Assert.Equal("Hello team, let's start the review.", result.Segment.Text);
        Assert.Equal("Speaker 1", result.Segment.SpeakerLabel);
        Assert.True(result.SpeakerTalkTime.ContainsKey("Speaker 1"));
        Assert.Equal(100.0, result.SpeakerTalkTime["Speaker 1"]);
    }

    [Fact]
    public async Task ProcessLiveStreamChunkAsync_WithInstantQuestion_ProducesRecallInsight()
    {
        var conversationId = Guid.NewGuid();
        using var stream = new MemoryStream(new byte[100]);
        var request = new LiveStreamChunkRequest
                      {
                          ChunkIndex      = 1
                        , OffsetSeconds   = 2.5
                        , DurationSeconds = 2.5
                      };

        var transcript = new Transcript
                         {
                             ConversationId = conversationId
                           , Status         = TranscriptionStatus.Completed
                           , Segments       = new List<TranscriptSegment>
                                              {
                                                  new() { Text = "What was Sarah's dog's name?" }
                                              }
                         };

        var candidateMemories = new List<ConversationMemoryCandidate>
                                {
                                    new()
                                    {
                                        Id       = Guid.NewGuid()
                                      , Category = "Fact"
                                      , Content  = "Sarah's dog is named Milo, a golden retriever."
                                    }
                                };

        _transcriptionServiceMock
            .Setup(ts => ts.TranscribeAudioAsync(conversationId, It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(transcript);

        _conversationServiceMock
            .Setup(cs => cs.QueryMemoriesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(candidateMemories);

        _objectStoreMock
            .Setup(os => os.GetAsync<List<CopilotInsight>>($"copilot_insights_{conversationId}", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CopilotInsight>());

        _objectStoreMock
            .Setup(os => os.GetAsync<Dictionary<string, double>>($"copilot_talktime_{conversationId}", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, double>());

        var result = await _service.ProcessLiveStreamChunkAsync(conversationId, stream, request);

        Assert.NotNull(result);
        Assert.True(result.HasActionableInsight);
        Assert.Single(result.Insights);
        Assert.Equal(CopilotInsightType.RecallHint, result.Insights[0].InsightType);
        Assert.Equal("Sarah's dog is named Milo, a golden retriever.", result.Insights[0].Detail);
    }
}
