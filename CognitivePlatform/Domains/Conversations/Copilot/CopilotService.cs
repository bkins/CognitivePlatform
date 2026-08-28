using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Domains.Personas.Models;
using CP.Shared.Primitives.Avails.Extensions;
using Microsoft.Extensions.Logging;

namespace CognitivePlatform.Api.Domains.Conversations.Copilot;

public class CopilotService : ICopilotService
{
    private static readonly Regex QuestionRegex = new(
        @"\b(what\s+(?:was|is|did|are|were)|who\s+(?:was|is|were)|where\s+(?:was|is|did)|when\s+(?:was|is|did)|how\s+(?:was|is|did)|do\s+you\s+remember|can\s+you\s+recall|tell\s+me\s+about)\b\s*(.+?)(?:\?|$)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CommitmentRegex = new(
        @"\b(i\s+will|i'll|we\s+will|we'll|i\s+promise|let\s+me\s+follow\s+up|i'll\s+send|let's\s+meet|deadline\s+is|action\s+item|by\s+friday|by\s+monday|by\s+tomorrow|i'll\s+make\s+sure)\b\s*(.+?)(?:\.|$)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly ITranscriptionService          _transcriptionService;
    private readonly IObjectStore                   _objectStore;
    private readonly IConversationService           _conversationService;
    private readonly ILogger<CopilotService>        _logger;

    public CopilotService( ITranscriptionService   transcriptionService
                         , IObjectStore            objectStore
                         , IConversationService    conversationService
                         , ILogger<CopilotService> logger )
    {
        _transcriptionService = transcriptionService;
        _objectStore          = objectStore;
        _conversationService  = conversationService;
        _logger               = logger;
    }

    public async Task<CopilotSliceResult> ProcessSliceAsync( Guid conversationId
                                                           , Stream audioStream
                                                           , CopilotSliceRequest request
                                                           , CancellationToken cancellationToken = default )
    {
        var result = new CopilotSliceResult
                     {
                         ConversationId = conversationId
                       , SliceIndex     = request.SliceIndex
                       , ProcessedAtUtc = DateTime.UtcNow
                     };

        if (audioStream == null || (audioStream.CanSeek && audioStream.Length == 0))
        {
            return result;
        }

        try
        {
            var transcript = await _transcriptionService.TranscribeAudioAsync(
                conversationId:    conversationId,
                audioStream:       audioStream,
                mimeType:          request.MimeType.HasValue() ? request.MimeType : "audio/wav",
                cancellationToken: cancellationToken);

            if (transcript?.Segments == null || transcript.Segments.Count == 0)
            {
                return result;
            }

            var sliceText = string.Join(" ", transcript.Segments.Select(segment => segment.Text)).Trim();
            result.TranscribedText = sliceText;

            if (sliceText.HasNoValue())
            {
                return result;
            }

            var detectedInsights = new List<CopilotInsight>();

            // 1. Evaluate Question / Interrogative Trigger
            var questionMatch = QuestionRegex.Match(sliceText);
            if (questionMatch.Success)
            {
                var querySubject = questionMatch.Groups[2].Value.Trim().TrimEnd('?', '.');
                if (querySubject.HasValue() && querySubject.Length >= 3)
                {
                    var memoryMatches = await _conversationService.QueryMemoriesAsync(querySubject, cancellationToken);
                    if (memoryMatches != null && memoryMatches.Count > 0)
                    {
                        var topMemory = memoryMatches.First();
                        detectedInsights.Add(new CopilotInsight
                        {
                            Id                 = Guid.NewGuid()
                          , ConversationId     = conversationId
                          , TimestampUtc       = DateTime.UtcNow
                          , AudioOffsetSeconds = request.OffsetSeconds
                          , InsightType        = CopilotInsightType.RecallHint
                          , Headline           = $"Memory Recall: {querySubject}"
                          , Detail             = topMemory.Content
                          , RelevanceScore     = 0.95f
                          , ProvenanceChain    = $"Memory:{topMemory.Id}|Category:{topMemory.Category}"
                        });
                    }
                }
            }

            // 2. Evaluate Commitment / Action Trigger
            var commitmentMatch = CommitmentRegex.Match(sliceText);
            if (commitmentMatch.Success)
            {
                var commitmentText = commitmentMatch.Value.Trim();
                detectedInsights.Add(new CopilotInsight
                {
                    Id                 = Guid.NewGuid()
                  , ConversationId     = conversationId
                  , TimestampUtc       = DateTime.UtcNow
                  , AudioOffsetSeconds = request.OffsetSeconds
                  , InsightType        = CopilotInsightType.CommitmentNotice
                  , Headline           = "Commitment Detected"
                  , Detail             = commitmentText
                  , RelevanceScore     = 0.90f
                  , ProvenanceChain    = $"Offset:{request.OffsetSeconds}s|Slice:{request.SliceIndex}"
                });
            }

            // 3. Evaluate Participant Mentions Context
            var participants = await _conversationService.GetParticipantsAsync(conversationId, cancellationToken);
            if (participants != null && participants.Count > 0)
            {
                foreach (var participant in participants)
                {
                    if (participant.DisplayName.HasValue() && sliceText.Contains(participant.DisplayName, StringComparison.OrdinalIgnoreCase))
                    {
                        var participantMemories = await _conversationService.QueryMemoriesAsync(participant.DisplayName, cancellationToken);
                        if (participantMemories != null && participantMemories.Count > 0)
                        {
                            var topParticipantMemory = participantMemories.First();
                            if (!detectedInsights.Any(insight => insight.Detail == topParticipantMemory.Content))
                            {
                                detectedInsights.Add(new CopilotInsight
                                {
                                    Id                 = Guid.NewGuid()
                                  , ConversationId     = conversationId
                                  , TimestampUtc       = DateTime.UtcNow
                                  , AudioOffsetSeconds = request.OffsetSeconds
                                  , InsightType        = CopilotInsightType.ContextFact
                                  , Headline           = $"Context: {participant.DisplayName}"
                                  , Detail             = topParticipantMemory.Content
                                  , RelevanceScore     = 0.85f
                                  , ProvenanceChain    = $"Participant:{participant.DisplayName}|Memory:{topParticipantMemory.Id}"
                                });
                            }
                        }
                    }
                }
            }

            if (detectedInsights.Count > 0)
            {
                var existingInsights = await GetInsightsAsync(conversationId, cancellationToken);
                var newInsights = new List<CopilotInsight>();

                foreach (var insight in detectedInsights)
                {
                    // Deduplicate against existing insights in this conversation
                    if (!existingInsights.Any(existing => existing.Headline.Equals(insight.Headline, StringComparison.OrdinalIgnoreCase)
                                                       && existing.Detail.Equals(insight.Detail, StringComparison.OrdinalIgnoreCase)))
                    {
                        existingInsights.Add(insight);
                        newInsights.Add(insight);
                    }
                }

                if (newInsights.Count > 0)
                {
                    await _objectStore.Save(existingInsights, partitionKey: null, id: $"copilot_insights_{conversationId}");
                    result.Insights = newInsights;
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process copilot slice for conversation {ConversationId}", conversationId);
            return result;
        }
    }

    public async Task<LiveStreamChunkResult> ProcessLiveStreamChunkAsync( Guid conversationId
                                                                         , Stream audioChunkStream
                                                                         , LiveStreamChunkRequest request
                                                                         , CancellationToken cancellationToken = default )
    {
        var result = new LiveStreamChunkResult
                     {
                         ConversationId = conversationId
                       , ChunkIndex     = request.ChunkIndex
                       , ProcessedAtUtc = DateTime.UtcNow
                     };

        if (audioChunkStream == null || (audioChunkStream.CanSeek && audioChunkStream.Length == 0))
        {
            return result;
        }

        try
        {
            var transcript = await _transcriptionService.TranscribeAudioAsync(
                conversationId:    conversationId,
                audioStream:       audioChunkStream,
                mimeType:          "audio/wav",
                cancellationToken: cancellationToken);

            var chunkText = transcript?.Segments != null && transcript.Segments.Count > 0
                ? string.Join(" ", transcript.Segments.Select(segment => segment.Text)).Trim()
                : string.Empty;

            if (chunkText.HasNoValue())
            {
                return result;
            }

            // Estimate Speaker turn attribution
            var speakerLabel = (request.ChunkIndex / 2) % 2 == 0 ? "Speaker 1" : "Speaker 2";

            var segment = new TranscriptSegment
                          {
                              Id           = Guid.NewGuid()
                            , Start        = TimeSpan.FromSeconds(request.OffsetSeconds)
                            , End          = TimeSpan.FromSeconds(request.OffsetSeconds + request.DurationSeconds)
                            , Text         = chunkText
                            , SpeakerId    = speakerLabel
                            , SpeakerLabel = speakerLabel
                          };
            result.Segment = segment;
            result.IsFinal = chunkText.EndsWith('.') || chunkText.EndsWith('?') || chunkText.EndsWith('!');

            // Maintain running speaker talk-time duration
            var talkTime = await _objectStore.GetAsync<Dictionary<string, double>>($"copilot_talktime_{conversationId}", partitionKey: null, cancellationToken: cancellationToken)
                           ?? new Dictionary<string, double>();

            if (!talkTime.ContainsKey(speakerLabel))
            {
                talkTime[speakerLabel] = 0;
            }
            talkTime[speakerLabel] += request.DurationSeconds > 0 ? request.DurationSeconds : 2.5;
            await _objectStore.Save(talkTime, partitionKey: null, id: $"copilot_talktime_{conversationId}");

            var totalTime = talkTime.Values.Sum();
            if (totalTime > 0)
            {
                foreach (var pair in talkTime)
                {
                    result.SpeakerTalkTime[pair.Key] = Math.Round((pair.Value / totalTime) * 100.0, 1);
                }
            }

            // Fast Sentence-Boundary Trigger Evaluation
            var detectedInsights = new List<CopilotInsight>();

            // 1. Question Trigger
            var questionMatch = QuestionRegex.Match(chunkText);
            if (questionMatch.Success)
            {
                var querySubject = questionMatch.Groups[2].Value.Trim().TrimEnd('?', '.');
                if (querySubject.HasValue() && querySubject.Length >= 3)
                {
                    var memoryMatches = await _conversationService.QueryMemoriesAsync(querySubject, cancellationToken);
                    if (memoryMatches != null && memoryMatches.Count > 0)
                    {
                        var topMemory = memoryMatches.First();
                        detectedInsights.Add(new CopilotInsight
                        {
                            Id                 = Guid.NewGuid()
                          , ConversationId     = conversationId
                          , TimestampUtc       = DateTime.UtcNow
                          , AudioOffsetSeconds = request.OffsetSeconds
                          , InsightType        = CopilotInsightType.RecallHint
                          , Headline           = $"Memory Recall: {querySubject}"
                          , Detail             = topMemory.Content
                          , RelevanceScore     = 0.95f
                          , ProvenanceChain    = $"Memory:{topMemory.Id}|Category:{topMemory.Category}"
                        });
                    }
                }
            }

            // 2. Commitment Trigger
            var commitmentMatch = CommitmentRegex.Match(chunkText);
            if (commitmentMatch.Success)
            {
                var commitmentText = commitmentMatch.Value.Trim();
                detectedInsights.Add(new CopilotInsight
                {
                    Id                 = Guid.NewGuid()
                  , ConversationId     = conversationId
                  , TimestampUtc       = DateTime.UtcNow
                  , AudioOffsetSeconds = request.OffsetSeconds
                  , InsightType        = CopilotInsightType.CommitmentNotice
                  , Headline           = "Commitment Detected"
                  , Detail             = commitmentText
                  , RelevanceScore     = 0.90f
                  , ProvenanceChain    = $"Offset:{request.OffsetSeconds}s|Chunk:{request.ChunkIndex}"
                });
            }

            if (detectedInsights.Count > 0)
            {
                var existingInsights = await GetInsightsAsync(conversationId, cancellationToken);
                var newInsights = new List<CopilotInsight>();

                foreach (var insight in detectedInsights)
                {
                    if (!existingInsights.Any(existing => existing.Headline.Equals(insight.Headline, StringComparison.OrdinalIgnoreCase)
                                                       && existing.Detail.Equals(insight.Detail, StringComparison.OrdinalIgnoreCase)))
                    {
                        existingInsights.Add(insight);
                        newInsights.Add(insight);
                    }
                }

                if (newInsights.Count > 0)
                {
                    await _objectStore.Save(existingInsights, partitionKey: null, id: $"copilot_insights_{conversationId}");
                    result.Insights = newInsights;
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process live stream chunk for conversation {ConversationId}", conversationId);
            return result;
        }
    }

    public async Task<List<CopilotInsight>> GetInsightsAsync( Guid conversationId
                                                           , CancellationToken cancellationToken = default )
    {
        var insights = await _objectStore.GetAsync<List<CopilotInsight>>($"copilot_insights_{conversationId}", partitionKey: null, cancellationToken: cancellationToken);
        if (insights == null)
        {
            return new List<CopilotInsight>();
        }

        return insights.Where(insight => !insight.IsDeleted).ToList();
    }

    public async Task<bool> DismissInsightAsync( Guid conversationId
                                               , Guid insightId
                                               , CancellationToken cancellationToken = default )
    {
        var insights = await GetInsightsAsync(conversationId, cancellationToken);
        var target = insights.FirstOrDefault(insight => insight.Id == insightId);
        if (target == null)
        {
            return false;
        }

        target.IsDismissed = true;
        await _objectStore.Save(insights, partitionKey: null, id: $"copilot_insights_{conversationId}");
        return true;
    }
}
