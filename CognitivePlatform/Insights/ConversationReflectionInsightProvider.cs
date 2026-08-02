using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using CognitivePlatform.Api.Conversation;
using CognitivePlatform.Api.Insights.Models;
using CognitivePlatform.Api.Interpreter;
using Microsoft.Extensions.Logging;

namespace CognitivePlatform.Api.Insights;

/// <summary>
/// Phase A reflection provider. Asks the active session LLM (via
/// <see cref="ILlmRouter"/>) whether the user's most recent message warrants a
/// gentle, observational suggestion — and if so, returns up to a small handful of
/// structured <see cref="Insight"/> objects.
///
/// <para>
/// Phase A scope limitation: the platform does not yet keep a multi-turn
/// conversation history on <see cref="ConversationContext"/>, so this provider
/// only sees <see cref="ConversationContext.LastUserMessage"/>. The
/// <see cref="InsightPolicy.MaxAnalysisTurns"/> /
/// <see cref="InsightPolicy.MaxAnalysisTokens"/> caps are accepted from policy
/// today and applied as a no-op; once a turn-history field lands on the context
/// (its own ticket), this provider opens the window without further plumbing.
/// </para>
///
/// Malformed JSON from the model is logged and yields zero insights — the
/// provider never throws into the engine. Provider-level exceptions still get
/// caught and counted by the engine's <see cref="InsightActivityTypes.ProviderFailed"/>
/// path.
/// </summary>
public sealed class ConversationReflectionInsightProvider : IInsightProvider
{
    private readonly ILlmRouter                                          _router;
    private readonly InsightPolicy                                       _policy;
    private readonly ILogger<ConversationReflectionInsightProvider>      _logger;

    private static readonly JsonSerializerOptions JsonOptions =
        new() { PropertyNameCaseInsensitive = true };

    public InsightCategory Category => InsightCategory.Reflection;

    public ConversationReflectionInsightProvider(
        ILlmRouter                                     router
      , InsightPolicy                                  policy
      , ILogger<ConversationReflectionInsightProvider> logger )
    {
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async IAsyncEnumerable<Insight> GenerateAsync(
        ConversationContext                        context
      , [EnumeratorCancellation] CancellationToken cancellationToken = default )
    {
        // Skip reflection insights if the last turn was a bug report or idea report
        if (context.Turns.Count > 0)
        {
            var lastTurn = context.Turns[^1];
            if (string.Equals(lastTurn.ActionName, "ReportBug",  StringComparison.OrdinalIgnoreCase)
             || string.Equals(lastTurn.ActionName, "ReportIdea", StringComparison.OrdinalIgnoreCase))
            {
                yield break;
            }
        }

        // Defensively skip if the user message starts with common bug or idea prefixes
        var lastUserMsg = context.LastUserMessage?.Trim();
        if (lastUserMsg != null && (lastUserMsg.StartsWith("bug:",            StringComparison.OrdinalIgnoreCase)
                                 || lastUserMsg.StartsWith("bug report:",     StringComparison.OrdinalIgnoreCase)
                                 || lastUserMsg.StartsWith("found a bug:",    StringComparison.OrdinalIgnoreCase)
                                 || lastUserMsg.StartsWith("report a bug:",   StringComparison.OrdinalIgnoreCase)
                                 || lastUserMsg.StartsWith("idea:",           StringComparison.OrdinalIgnoreCase)
                                 || lastUserMsg.StartsWith("new idea:",       StringComparison.OrdinalIgnoreCase)
                                 || lastUserMsg.StartsWith("idea suggestion:",StringComparison.OrdinalIgnoreCase)
                                 || lastUserMsg.StartsWith("suggest an idea:",StringComparison.OrdinalIgnoreCase)
                                 || lastUserMsg.StartsWith("report an idea:", StringComparison.OrdinalIgnoreCase)
                                 || lastUserMsg.StartsWith("report idea:",    StringComparison.OrdinalIgnoreCase)
                                 || lastUserMsg.StartsWith("feature idea:",   StringComparison.OrdinalIgnoreCase)
                                 || lastUserMsg.StartsWith("feature request:",StringComparison.OrdinalIgnoreCase)
                                 || lastUserMsg.StartsWith("suggestion:",     StringComparison.OrdinalIgnoreCase)
                                 || lastUserMsg.StartsWith("feature:",        StringComparison.OrdinalIgnoreCase)))
        {
            yield break;
        }

        var prompt = BuildPromptForContext(context);
        if (prompt is null)
            yield break;

        var response = (await _router.SendAsync(prompt
                                              , context
                                              , complexity: TaskComplexity.Light
                                              , ct:         cancellationToken)).Content;

        var parsed = TryParse(response, context.SessionId);
        if (parsed is null)
            yield break;

        foreach (var insight in parsed)
            yield return insight;
    }

    private string? BuildPromptForContext(ConversationContext context)
    {
        // ENH-08: prefer the Turns window when populated; fall back to LastUserMessage
        // if the orchestrator hasn't recorded any turns yet (defensive — shouldn't
        // happen in normal flow because RecordTurn runs before the engine call).
        var turns = context.Turns;
        if (turns.Count == 0)
        {
            var fallback = context.LastUserMessage;
            return string.IsNullOrWhiteSpace(fallback)
                           ? null
                           : BuildReflectionPrompt(fallback);
        }

        var window = SelectAnalysisWindow(turns);
        if (window.Count == 0)
            return null;

        return BuildReflectionPromptFromWindow(window);
    }

    /// <summary>
    /// Walks <paramref name="turns"/> in reverse, accumulating the most recent entries
    /// until either <see cref="InsightPolicy.MaxAnalysisTurns"/> or
    /// <see cref="InsightPolicy.MaxAnalysisTokens"/> is hit. Token estimation uses a
    /// chars-÷-4 heuristic — accurate enough for budget enforcement, no tokenizer
    /// dependency required.
    /// </summary>
    private List<ConversationTurn> SelectAnalysisWindow(IReadOnlyList<ConversationTurn> turns)
    {
        var window     = new List<ConversationTurn>();
        var charBudget = checked(_policy.MaxAnalysisTokens * 4);
        var charsUsed  = 0;

        for (var i = turns.Count - 1; i >= 0; i--)
        {
            if (window.Count >= _policy.MaxAnalysisTurns)
                break;

            var turn      = turns[i];
            var turnChars = (turn.UserMessage?.Length ?? 0) + (turn.AssistantMessage?.Length ?? 0);

            // Always include the newest turn even if it alone exceeds the budget;
            // otherwise an oversized message would silently produce zero insights.
            if (window.Count > 0 && charsUsed + turnChars > charBudget)
                break;

            window.Add(turn);
            charsUsed += turnChars;
        }

        window.Reverse();
        return window;
    }

    // ---------------------------------------------------------------------
    // Prompt construction
    // ---------------------------------------------------------------------

    private const string ReflectionPromptTemplate = """
        You are a reflective observer for a personal-assistant platform. Read the user's
        most recent message and decide whether it warrants a gentle, observational
        suggestion the assistant could offer (e.g. "want to log that as a journal entry?").

        Rules:
        - Stay quiet by default. Most messages should produce ZERO insights. Only
          surface a suggestion when the message clearly carries emotion, stress,
          reflection, or signals the user might benefit from journaling.
        - Never invent facts not present in the message.
        - Output STRICT JSON only — no prose, no markdown fences, no commentary.

        Output schema:
        {
          "insights": [
            {
              "message":          "<one short, conversational suggestion>",
              "suggestedAction":  "AddJournalEntry" | null,
              "deduplicationKey": "reflection.<short-signal>"
            }
          ]
        }

        If nothing is worth surfacing, output exactly:
        { "insights": [] }

        User's most recent message:
        ---
        {{USER_MESSAGE}}
        ---
        """;

    private const string MultiTurnReflectionPromptTemplate = """
        You are a reflective observer for a personal-assistant platform. Read the recent
        conversation turns below and decide whether the user's latest message warrants a
        gentle, observational suggestion the assistant could offer (e.g. "want to log
        that as a journal entry?").

        Rules:
        - Focus on the latest user message; use earlier turns only as context for
          interpreting it.
        - Stay quiet by default. Most messages should produce ZERO insights. Only
          surface a suggestion when the latest message clearly carries emotion, stress,
          reflection, or signals the user might benefit from journaling.
        - Some turns may be truncated (you'll see "...[truncated for context]"). Do not
          attempt to reconstruct what was cut.
        - Never invent facts not present in the conversation.
        - Output STRICT JSON only — no prose, no markdown fences, no commentary.

        Output schema:
        {
          "insights": [
            {
              "message":          "<one short, conversational suggestion>",
              "suggestedAction":  "AddJournalEntry" | null,
              "deduplicationKey": "reflection.<short-signal>"
            }
          ]
        }

        If nothing is worth surfacing, output exactly:
        { "insights": [] }

        Recent conversation (oldest first):
        ---
        {{TURNS}}
        ---
        """;

    private static string BuildReflectionPrompt(string lastUserMessage)
    {
        var prompt = new StringBuilder(ReflectionPromptTemplate.Length + lastUserMessage.Length);
        prompt.Append(ReflectionPromptTemplate.Replace("{{USER_MESSAGE}}", lastUserMessage));
        return prompt.ToString();
    }

    private static string BuildReflectionPromptFromWindow(IReadOnlyList<ConversationTurn> window)
    {
        var rendered = new StringBuilder();
        foreach (var turn in window)
        {
            rendered.Append("User: ");
            rendered.AppendLine(turn.UserMessage);
            rendered.Append("Assistant: ");
            rendered.AppendLine(turn.AssistantMessage);
            rendered.AppendLine();
        }

        return MultiTurnReflectionPromptTemplate.Replace("{{TURNS}}", rendered.ToString().TrimEnd());
    }

    // ---------------------------------------------------------------------
    // Response parsing
    // ---------------------------------------------------------------------

    private List<Insight>? TryParse(string rawResponse, string sessionId)
    {
        if (string.IsNullOrWhiteSpace(rawResponse))
            return null;

        var json = ExtractJsonObject(rawResponse);
        if (json is null)
        {
            _logger.LogDebug("Reflection provider: no JSON object found in response. Raw={Raw}"
                           , rawResponse);
            return null;
        }

        try
        {
            var envelope = JsonSerializer.Deserialize<ReflectionEnvelope>(json, JsonOptions);
            if (envelope?.Insights is null || envelope.Insights.Count == 0)
                return null;

            var sanitised = new List<Insight>(envelope.Insights.Count);
            foreach (var item in envelope.Insights)
            {
                if (string.IsNullOrWhiteSpace(item.Message))
                    continue;

                var deduplicationKey = string.IsNullOrWhiteSpace(item.DeduplicationKey)
                                               ? $"reflection.unspecified.{sessionId}"
                                               : $"{item.DeduplicationKey}.{sessionId}";

                sanitised.Add(new Insight
                              {
                                      Message          = item.Message
                                    , SuggestedAction  = string.IsNullOrWhiteSpace(item.SuggestedAction)
                                                                 ? null
                                                                 : item.SuggestedAction
                                    , Priority         = InsightPriority.Normal
                                    , Category         = InsightCategory.Reflection
                                    , DeduplicationKey = deduplicationKey
                              });
            }

            return sanitised.Count == 0 ? null : sanitised;
        }
        catch (JsonException ex)
        {
            _logger.LogDebug(ex
                           , "Reflection provider: malformed JSON in model response. Raw={Raw}"
                           , rawResponse);
            return null;
        }
    }

    /// <summary>
    /// Extracts the first balanced JSON object substring. Tolerant of leading/trailing
    /// prose or markdown fences the model occasionally wraps around its output.
    /// </summary>
    private static string? ExtractJsonObject(string raw)
    {
        var start = raw.IndexOf('{');
        if (start < 0) return null;

        var depth = 0;
        for (var i = start; i < raw.Length; i++)
        {
            switch (raw[i])
            {
                case '{':
                    depth++;
                    break;
                case '}':
                    depth--;
                    if (depth == 0)
                        return raw.Substring(start, i - start + 1);
                    break;
            }
        }

        return null;
    }

    private sealed class ReflectionEnvelope
    {
        public List<ReflectionItem>? Insights { get; set; }
    }

    private sealed class ReflectionItem
    {
        public string? Message          { get; set; }
        public string? SuggestedAction  { get; set; }
        public string? DeduplicationKey { get; set; }
    }
}
