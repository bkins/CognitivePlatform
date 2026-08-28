using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using CognitivePlatform.Api.Domains.Personas.Models;
using CognitivePlatform.Api.Interpreter;
using CP.Shared.Primitives.Avails.Extensions;
using Microsoft.Extensions.Logging;

namespace CognitivePlatform.Api.Domains.Conversations;

/// <summary>
/// LLM-powered cognitive memory extractor that analyzes conversation transcripts and
/// structured analyses to extract provisional memory candidates with segment-level provenance.
///
/// Invariant: Memory extraction produces provisional candidates; never mutates canonical identity.
/// </summary>
public sealed class LlmConversationMemoryExtractor : IConversationMemoryExtractor
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
      , DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly ILlmClientFactory                         _llmClientFactory;
    private readonly ILogger<LlmConversationMemoryExtractor>   _logger;

    public LlmConversationMemoryExtractor( ILlmClientFactory                       llmClientFactory
                                         , ILogger<LlmConversationMemoryExtractor> logger )
    {
        _llmClientFactory = llmClientFactory;
        _logger           = logger;
    }

    public async Task<List<ConversationMemoryCandidate>> ExtractMemoriesAsync( ConversationDetails details
                                                                             , CancellationToken cancellationToken = default )
    {
        var conversationId = details.Record.Id;

        if (details.Transcript == null || details.Transcript.Segments.Count == 0)
        {
            return new List<ConversationMemoryCandidate>();
        }

        try
        {
            var prompt   = BuildPrompt(details);
            var client   = _llmClientFactory.Create();
            var response = await client.SendAsync(prompt, model: null, cancellationToken);

            var candidates = ParseResponse(response.Content, conversationId, details.Analysis?.Id, details.Transcript.Segments);
            if (candidates.Count > 0)
            {
                return candidates;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LLM memory extraction failed for conversation {ConversationId}. Using heuristic fallback.", conversationId);
        }

        // Resilient fallback: derive candidates directly from structured analysis if available
        return DeriveFromAnalysis(details);
    }

    internal string BuildPrompt( ConversationDetails details )
    {
        var builder = new StringBuilder();

        builder.AppendLine("You are a cognitive memory extraction engine. Analyze the following conversation transcript and extract persistent memory candidates.");
        builder.AppendLine();
        builder.AppendLine($"Conversation Title: {details.Record.Title}");
        builder.AppendLine($"Recorded: {details.Record.RecordedAtUtc:yyyy-MM-dd HH:mm} UTC");
        builder.AppendLine();

        if (details.Participants.Count > 0)
        {
            builder.AppendLine("Participants:");
            foreach (var participant in details.Participants)
            {
                builder.AppendLine($"  - {participant.DisplayName ?? participant.SpeakerLabel} ({participant.SpeakerLabel})");
            }
            builder.AppendLine();
        }

        if (details.Analysis != null && details.Analysis.Summary.HasValue())
        {
            builder.AppendLine($"Summary: {details.Analysis.Summary}");
            builder.AppendLine();
        }

        builder.AppendLine("Transcript segments (indexed):");
        for (int i = 0; i < details.Transcript!.Segments.Count; i++)
        {
            var segment     = details.Transcript.Segments[i];
            var speakerName = segment.SpeakerName ?? segment.SpeakerLabel;
            builder.AppendLine($"[{i}] {speakerName}: {segment.Text}");
        }

        builder.AppendLine();
        builder.AppendLine("Extract durable memory candidates belonging to one of these categories:");
        builder.AppendLine("- Fact: Long-term truth or factual information mentioned.");
        builder.AppendLine("- Commitment: A promise, obligation, or assignment made by a participant.");
        builder.AppendLine("- Preference: A stated like, dislike, habit, or preference.");
        builder.AppendLine("- Decision: A conclusion or policy agreed upon.");
        builder.AppendLine("- Plan: A future event, milestone, or intention.");
        builder.AppendLine("- Context: Key background relationship or environmental detail.");
        builder.AppendLine();
        builder.AppendLine("Respond with ONLY a JSON array with this schema:");
        builder.AppendLine("""
[
  {
    "category": "Fact",
    "content": "Clear statement of the memory",
    "speaker": "Speaker name if applicable",
    "segmentIndices": [0, 1],
    "confidence": 0.95
  }
]
""");
        builder.AppendLine("Rules:");
        builder.AppendLine("- Extract only high-value, durable memories worthy of long-term recollection.");
        builder.AppendLine("- Each item must list the supporting segment indices from the transcript.");
        builder.AppendLine("- Do not fabricate or speculate.");

        return builder.ToString();
    }

    internal List<ConversationMemoryCandidate> ParseResponse( string responseContent
                                                            , Guid conversationId
                                                            , Guid? analysisId
                                                            , List<TranscriptSegment> segments )
    {
        var json = ExtractJsonArray(responseContent);
        if (json.HasNoValue())
        {
            return new List<ConversationMemoryCandidate>();
        }

        try
        {
            var rawItems = JsonSerializer.Deserialize<List<RawMemoryItem>>(json, JsonOptions);
            if (rawItems == null || rawItems.Count == 0)
            {
                return new List<ConversationMemoryCandidate>();
            }

            return rawItems.Select(raw =>
            {
                var segmentIds = (raw.SegmentIndices ?? new List<int>())
                    .Where(index => index >= 0 && index < segments.Count)
                    .Select(index => segments[index].Id)
                    .ToList();

                return new ConversationMemoryCandidate
                {
                    Id                         = Guid.NewGuid()
                  , ConversationId             = conversationId
                  , AnalysisId                 = analysisId
                  , Category                   = NormalizeCategory(raw.Category)
                  , Content                    = raw.Content ?? string.Empty
                  , Speaker                    = raw.Speaker
                  , SourceTranscriptSegmentIds = segmentIds
                  , Confidence                 = raw.Confidence > 0 ? raw.Confidence : 1.0
                  , ExtractedAtUtc             = DateTime.UtcNow
                  , State                      = MemoryState.Provisional
                };
            }).Where(candidate => candidate.Content.HasValue()).ToList();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse memory extraction JSON for conversation {ConversationId}", conversationId);
            return new List<ConversationMemoryCandidate>();
        }
    }

    private static List<ConversationMemoryCandidate> DeriveFromAnalysis( ConversationDetails details )
    {
        var candidates     = new List<ConversationMemoryCandidate>();
        var conversationId = details.Record.Id;
        var analysis       = details.Analysis;

        if (analysis == null)
        {
            return candidates;
        }

        foreach (var decision in analysis.Decisions)
        {
            candidates.Add(new ConversationMemoryCandidate
            {
                Id                         = Guid.NewGuid()
              , ConversationId             = conversationId
              , AnalysisId                 = analysis.Id
              , Category                   = "Decision"
              , Content                    = decision.Content
              , SourceTranscriptSegmentIds = decision.SourceTranscriptSegmentIds
              , Confidence                 = 0.9
              , ExtractedAtUtc             = DateTime.UtcNow
              , State                      = MemoryState.Provisional
            });
        }

        foreach (var action in analysis.ActionItems)
        {
            candidates.Add(new ConversationMemoryCandidate
            {
                Id                         = Guid.NewGuid()
              , ConversationId             = conversationId
              , AnalysisId                 = analysis.Id
              , Category                   = "Commitment"
              , Content                    = action.Content
              , SourceTranscriptSegmentIds = action.SourceTranscriptSegmentIds
              , Confidence                 = 0.9
              , ExtractedAtUtc             = DateTime.UtcNow
              , State                      = MemoryState.Provisional
            });
        }

        foreach (var statement in analysis.ImportantStatements)
        {
            candidates.Add(new ConversationMemoryCandidate
            {
                Id                         = Guid.NewGuid()
              , ConversationId             = conversationId
              , AnalysisId                 = analysis.Id
              , Category                   = "Fact"
              , Content                    = statement.Content
              , SourceTranscriptSegmentIds = statement.SourceTranscriptSegmentIds
              , Confidence                 = 0.85
              , ExtractedAtUtc             = DateTime.UtcNow
              , State                      = MemoryState.Provisional
            });
        }

        return candidates;
    }

    private static string ExtractJsonArray( string content )
    {
        if (content.HasNoValue())
        {
            return "[]";
        }

        var trimmed = content.Trim();
        var start   = trimmed.IndexOf('[');
        var end     = trimmed.LastIndexOf(']');
        if (start >= 0 && end > start)
        {
            return trimmed[start..(end + 1)];
        }

        return trimmed;
    }

    private static string NormalizeCategory( string? category )
    {
        if (category.HasNoValue())
        {
            return "Fact";
        }

        return category!.Trim().ToLowerInvariant() switch
        {
            "fact"        => "Fact"
          , "commitment"  => "Commitment"
          , "preference"  => "Preference"
          , "decision"    => "Decision"
          , "plan"        => "Plan"
          , "context"     => "Context"
          , _             => "Fact"
        };
    }

    private sealed class RawMemoryItem
    {
        [JsonPropertyName("category")]       public string?    Category       { get; set; }
        [JsonPropertyName("content")]        public string?    Content        { get; set; }
        [JsonPropertyName("speaker")]        public string?    Speaker        { get; set; }
        [JsonPropertyName("segmentIndices")] public List<int>? SegmentIndices { get; set; }
        [JsonPropertyName("confidence")]     public double     Confidence     { get; set; }
    }
}
