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
        var lastMessage = context.LastUserMessage;
        if (string.IsNullOrWhiteSpace(lastMessage))
            yield break;

        var prompt   = BuildReflectionPrompt(lastMessage);
        var response = await _router.SendAsync(prompt, context, cancellationToken);

        var parsed = TryParse(response, context.SessionId);
        if (parsed is null)
            yield break;

        foreach (var insight in parsed)
            yield return insight;
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

    private string BuildReflectionPrompt(string lastUserMessage)
    {
        // Phase A: single-turn window. The MaxAnalysisTurns / MaxAnalysisTokens caps
        // are referenced here so the dependency is wired and the configuration is
        // exercised, even though the loop only runs over one turn today.
        _ = _policy.MaxAnalysisTurns;
        _ = _policy.MaxAnalysisTokens;

        var prompt = new StringBuilder(ReflectionPromptTemplate.Length + lastUserMessage.Length);
        prompt.Append(ReflectionPromptTemplate.Replace("{{USER_MESSAGE}}", lastUserMessage));
        return prompt.ToString();
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
