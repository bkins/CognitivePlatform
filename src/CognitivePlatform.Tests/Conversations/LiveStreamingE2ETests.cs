using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Domains.Conversations;
using CognitivePlatform.Api.Domains.Conversations.Copilot;
using CognitivePlatform.Api.Domains.Personas.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace CognitivePlatform.Tests.Conversations;

public class LiveStreamingE2ETests
{
    private readonly Mock<ITranscriptionService> _transcriptionMock = new();
    private readonly Mock<IConversationService>  _conversationServiceMock = new();
    private readonly Mock<IObjectStore>          _objectStoreMock = new();
    private readonly Dictionary<string, object>  _stateStore = new();
    private readonly CopilotService              _copilotService;

    public LiveStreamingE2ETests()
    {
        _objectStoreMock
            .Setup(os => os.Save(It.IsAny<object>(), It.IsAny<string>(), It.IsAny<string>()))
            .Callback<object, string, string>((obj, partitionKey, id) =>
            {
                _stateStore[id] = obj;
            });

        _objectStoreMock
            .Setup(os => os.GetAsync<It.IsAnyType>(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(new InvocationFunc(invocation =>
            {
                var id = (string)invocation.Arguments[0];
                var returnType = invocation.Method.GetGenericArguments()[0];

                if (_stateStore.TryGetValue(id, out var val))
                {
                    var taskResultType = typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(returnType);
                    return taskResultType.Invoke(null, new[] { val })!;
                }

                var defaultTask = typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(returnType);
                return defaultTask.Invoke(null, new object?[] { null })!;
            }));

        _copilotService = new CopilotService(
            _transcriptionMock.Object,
            _objectStoreMock.Object,
            _conversationServiceMock.Object,
            NullLogger<CopilotService>.Instance);
    }

    [Fact]
    public async Task EndToEnd_LiveStreamingMultiTurnSession_ProcessesChunks_UpdatesTalkTime_AndRecallsMemories()
    {
        var conversationId = Guid.NewGuid();

        // 1. Setup Memory Candidate for Recall
        var candidateMemories = new List<ConversationMemoryCandidate>
                                {
                                    new()
                                    {
                                        Id       = Guid.NewGuid()
                                      , Category = "Fact"
                                      , Content  = "Sarah's dog is named Milo, a golden retriever."
                                    }
                                };

        _conversationServiceMock
            .Setup(cs => cs.QueryMemoriesAsync("Sarah's dog's name", It.IsAny<CancellationToken>()))
            .ReturnsAsync(candidateMemories);

        _conversationServiceMock
            .Setup(cs => cs.GetParticipantsAsync(conversationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ConversationParticipant>());

        // 2. Stream Chunk 0: Speaker 1 introduction
        using var chunk0Audio = new MemoryStream(new byte[100]);
        _transcriptionMock
            .Setup(ts => ts.TranscribeAudioAsync(conversationId, chunk0Audio, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Transcript
                          {
                              ConversationId = conversationId
                            , Status         = TranscriptionStatus.Completed
                            , Segments       = new List<TranscriptSegment> { new() { Text = "Hello team, let us start the architecture sync." } }
                          });

        var chunk0Result = await _copilotService.ProcessLiveStreamChunkAsync(
            conversationId:   conversationId,
            audioChunkStream: chunk0Audio,
            request:          new LiveStreamChunkRequest { ChunkIndex = 0, OffsetSeconds = 0.0, DurationSeconds = 2.5 });

        Assert.NotNull(chunk0Result.Segment);
        Assert.Equal("Speaker 1", chunk0Result.Segment.SpeakerLabel);
        Assert.Equal(100.0, chunk0Result.SpeakerTalkTime["Speaker 1"]);
        Assert.False(chunk0Result.HasActionableInsight);

        // 3. Stream Chunk 1: Speaker 1 continues with question
        using var chunk1Audio = new MemoryStream(new byte[100]);
        _transcriptionMock
            .Setup(ts => ts.TranscribeAudioAsync(conversationId, chunk1Audio, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Transcript
                          {
                              ConversationId = conversationId
                            , Status         = TranscriptionStatus.Completed
                            , Segments       = new List<TranscriptSegment> { new() { Text = "What was Sarah's dog's name?" } }
                          });

        var chunk1Result = await _copilotService.ProcessLiveStreamChunkAsync(
            conversationId:   conversationId,
            audioChunkStream: chunk1Audio,
            request:          new LiveStreamChunkRequest { ChunkIndex = 1, OffsetSeconds = 2.5, DurationSeconds = 2.5 });

        Assert.NotNull(chunk1Result.Segment);
        Assert.True(chunk1Result.HasActionableInsight);
        Assert.Single(chunk1Result.Insights);
        Assert.Equal(CopilotInsightType.RecallHint, chunk1Result.Insights[0].InsightType);
        Assert.Equal("Sarah's dog is named Milo, a golden retriever.", chunk1Result.Insights[0].Detail);

        // 4. Stream Chunk 2: Speaker 2 speaks and makes commitment
        using var chunk2Audio = new MemoryStream(new byte[100]);
        _transcriptionMock
            .Setup(ts => ts.TranscribeAudioAsync(conversationId, chunk2Audio, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Transcript
                          {
                              ConversationId = conversationId
                            , Status         = TranscriptionStatus.Completed
                            , Segments       = new List<TranscriptSegment> { new() { Text = "I will send the meeting summary by Friday." } }
                          });

        var chunk2Result = await _copilotService.ProcessLiveStreamChunkAsync(
            conversationId:   conversationId,
            audioChunkStream: chunk2Audio,
            request:          new LiveStreamChunkRequest { ChunkIndex = 2, OffsetSeconds = 5.0, DurationSeconds = 2.5 });

        Assert.NotNull(chunk2Result.Segment);
        Assert.Equal("Speaker 2", chunk2Result.Segment.SpeakerLabel);
        Assert.True(chunk2Result.HasActionableInsight);
        Assert.Equal(CopilotInsightType.CommitmentNotice, chunk2Result.Insights[0].InsightType);

        // 5. Verify Cumulative Speaker Talk-Time Ratio
        Assert.Equal(66.7, chunk2Result.SpeakerTalkTime["Speaker 1"]);
        Assert.Equal(33.3, chunk2Result.SpeakerTalkTime["Speaker 2"]);

        // 6. Verify Persistent Insight Store Accumulation and Dismissal
        var allInsights = await _copilotService.GetInsightsAsync(conversationId);
        Assert.Equal(2, allInsights.Count);

        var firstInsightId = allInsights[0].Id;
        var dismissed = await _copilotService.DismissInsightAsync(conversationId, firstInsightId);
        Assert.True(dismissed);

        var activeInsights = await _copilotService.GetInsightsAsync(conversationId);
        var dismissedInsight = activeInsights.Find(insight => insight.Id == firstInsightId);
        Assert.NotNull(dismissedInsight);
        Assert.True(dismissedInsight.IsDismissed);
    }
}
