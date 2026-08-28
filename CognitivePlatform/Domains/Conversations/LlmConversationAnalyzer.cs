using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CognitivePlatform.Api.Interpreter;
using CP.Shared.Primitives.Avails.Extensions;
using Microsoft.Extensions.Logging;

namespace CognitivePlatform.Api.Domains.Conversations;

/// <summary>
/// LLM-powered conversation analyzer that produces structured understanding
/// from completed transcripts. Uses the system-default LLM provider via
/// <see cref="ILlmClientFactory"/> for single-shot structured JSON extraction.
///
/// Invariant: Transcript = evidence (ground truth). Analysis = interpretation (inference).
/// The analyzer never mutates transcript data.
/// </summary>
public class LlmConversationAnalyzer : IConversationAnalyzer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
      , DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly ILlmClientFactory                   _llmClientFactory;
    private readonly ILogger<LlmConversationAnalyzer>    _logger;

    public LlmConversationAnalyzer( ILlmClientFactory                llmClientFactory
                                  , ILogger<LlmConversationAnalyzer> logger )
    {
        _llmClientFactory = llmClientFactory;
        _logger           = logger;
    }

    public async Task<ConversationAnalysis> AnalyzeAsync( ConversationDetails details
                                                        , CancellationToken cancellationToken = default )
    {
        var conversationId = details.Record.Id;
        var analysis = new ConversationAnalysis
        {
            ConversationId = conversationId
          , Status         = AnalysisStatus.Analyzing
        };

        if (details.Transcript == null || details.Transcript.Segments.Count == 0)
        {
            analysis.Status       = AnalysisStatus.Failed;
            analysis.ErrorMessage = "No transcript segments available for analysis.";
            return analysis;
        }

        try
        {
            var prompt   = BuildPrompt(details);
            var client   = _llmClientFactory.Create();
            var response = await client.SendAsync(prompt, model: null, cancellationToken);

            var parsed = ParseAnalysisResponse(response.Content, conversationId, details.Transcript.Segments);

            analysis.Summary             = parsed.Summary;
            analysis.Topics              = parsed.Topics;
            analysis.Questions           = parsed.Questions;
            analysis.Decisions           = parsed.Decisions;
            analysis.ActionItems         = parsed.ActionItems;
            analysis.ImportantStatements = parsed.ImportantStatements;
            analysis.Status              = AnalysisStatus.Completed;
            analysis.AnalyzedAtUtc       = DateTime.UtcNow;
            analysis.ModelUsed           = $"{_llmClientFactory.DefaultProvider}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Conversation analysis failed for {ConversationId}", conversationId);
            analysis.Status       = AnalysisStatus.Failed;
            analysis.ErrorMessage = $"Analysis failed: {ex.Message}";
        }

        return analysis;
    }

    internal string BuildPrompt( ConversationDetails details )
    {
        var builder = new StringBuilder();

        builder.AppendLine("You are a conversation analysis engine. Analyze the following conversation transcript and produce structured output.");
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

        builder.AppendLine("Transcript segments (indexed for reference):");
        builder.AppendLine();

        for (int i = 0; i < details.Transcript!.Segments.Count; i++)
        {
            var segment     = details.Transcript.Segments[i];
            var speakerName = segment.SpeakerName ?? segment.SpeakerLabel;
            var startTime   = FormatTimeSpan(segment.Start);
            var endTime     = FormatTimeSpan(segment.End);
            builder.AppendLine($"[{i}] [{startTime} - {endTime}] {speakerName}: {segment.Text}");
        }

        builder.AppendLine();
        builder.AppendLine("Produce a JSON response with the following structure. For each derived item, include the segment indices (from the transcript above) that support or relate to it.");
        builder.AppendLine();
        builder.AppendLine("Respond with ONLY valid JSON, no markdown code fences or other formatting:");
        builder.AppendLine("""
{
  "summary": "A concise human-readable overview of the conversation.",
  "topics": [
    { "content": "Topic description", "segmentIndices": [0, 1, 3] }
  ],
  "questions": [
    { "content": "Question that was asked or left unresolved", "segmentIndices": [2] }
  ],
  "decisions": [
    { "content": "Decision or conclusion reached", "segmentIndices": [4, 5] }
  ],
  "actionItems": [
    { "content": "Task or commitment identified", "segmentIndices": [6] }
  ],
  "importantStatements": [
    { "content": "Significant statement not captured above", "segmentIndices": [7] }
  ]
}
""");
        builder.AppendLine("Rules:");
        builder.AppendLine("- Only include items that are genuinely present in the conversation.");
        builder.AppendLine("- If a category has no items, return an empty array for that category.");
        builder.AppendLine("- Segment indices must reference valid indices from the transcript above.");
        builder.AppendLine("- The summary should be 2-4 sentences capturing the key points.");
        builder.AppendLine("- Be precise and factual — do not fabricate or infer beyond what was said.");

        return builder.ToString();
    }

    internal ConversationAnalysis ParseAnalysisResponse( string responseContent
                                                       , Guid conversationId
                                                       , List<TranscriptSegment> segments )
    {
        var analysis = new ConversationAnalysis { ConversationId = conversationId };

        var jsonContent = ExtractJson(responseContent);

        try
        {
            var parsed = JsonSerializer.Deserialize<RawAnalysisResponse>(jsonContent, JsonOptions);
            if (parsed == null)
            {
                analysis.Status       = AnalysisStatus.Failed;
                analysis.ErrorMessage = "LLM returned null or unparseable analysis response.";
                return analysis;
            }

            analysis.Summary = parsed.Summary ?? string.Empty;

            analysis.Topics              = MapDerivedItems(parsed.Topics, "Topic", conversationId, segments);
            analysis.Questions           = MapDerivedItems(parsed.Questions, "Question", conversationId, segments);
            analysis.Decisions           = MapDerivedItems(parsed.Decisions, "Decision", conversationId, segments);
            analysis.ActionItems         = MapDerivedItems(parsed.ActionItems, "ActionItem", conversationId, segments);
            analysis.ImportantStatements = MapDerivedItems(parsed.ImportantStatements, "ImportantStatement", conversationId, segments);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse analysis JSON for conversation {ConversationId}", conversationId);
            analysis.Status       = AnalysisStatus.Failed;
            analysis.ErrorMessage = $"Failed to parse LLM response as JSON: {ex.Message}";
        }

        return analysis;
    }

    private static List<AnalysisDerivedItem> MapDerivedItems( List<RawDerivedItem>?   rawItems
                                                            , string                  itemType
                                                            , Guid                    conversationId
                                                            , List<TranscriptSegment>  segments )
    {
        if (rawItems == null || rawItems.Count == 0)
        {
            return new List<AnalysisDerivedItem>();
        }

        return rawItems.Select(raw =>
        {
            var segmentIds = (raw.SegmentIndices ?? new List<int>())
                .Where(index => index >= 0 && index < segments.Count)
                .Select(index => segments[index].Id)
                .ToList();

            return new AnalysisDerivedItem
            {
                Id                         = Guid.NewGuid()
              , ConversationId             = conversationId
              , Type                       = itemType
              , Content                    = raw.Content ?? string.Empty
              , SourceTranscriptSegmentIds = segmentIds
            };
        }).ToList();
    }

    private static string ExtractJson( string content )
    {
        if (content.HasNoValue())
        {
            return "{}";
        }

        var trimmed = content.Trim();
        var start   = trimmed.IndexOf('{');
        var end     = trimmed.LastIndexOf('}');
        if (start >= 0 && end > start)
        {
            return trimmed[start..(end + 1)];
        }

        return trimmed;
    }

    private static string FormatTimeSpan( TimeSpan time )
    {
        return time.TotalHours >= 1
            ? $"{(int)time.TotalHours:D2}:{time.Minutes:D2}:{time.Seconds:D2}"
            : $"{time.Minutes:D2}:{time.Seconds:D2}";
    }

    // ──────────────────────────────────────────────
    //  Internal DTOs for JSON deserialization
    // ──────────────────────────────────────────────

    internal class RawAnalysisResponse
    {
        public string?              Summary             { get; set; }
        public List<RawDerivedItem>? Topics              { get; set; }
        public List<RawDerivedItem>? Questions           { get; set; }
        public List<RawDerivedItem>? Decisions           { get; set; }
        public List<RawDerivedItem>? ActionItems         { get; set; }
        public List<RawDerivedItem>? ImportantStatements { get; set; }
    }

    internal class RawDerivedItem
    {
        public string?     Content        { get; set; }
        public List<int>?  SegmentIndices { get; set; }
    }
}
