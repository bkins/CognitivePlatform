using System.Runtime.CompilerServices;
using CognitivePlatform.Api.Conversation;
using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Insights.Models;

namespace CognitivePlatform.Api.Insights;

/// <summary>
/// Phase A provider — no Object Store dependency, session context only.
/// Detects emotion/stress language in the user's last message and suggests
/// logging a journal entry.
/// </summary>
public sealed class ConversationReflectionInsightProvider : IInsightProvider
{
    public InsightCategory Category => InsightCategory.Reflection;

    private static readonly string[] StressKeywords =
    [
        "overwhelmed"
      , "stressed"
      , "anxious"
      , "exhausted"
      , "burned out"
      , "burnt out"
      , "frustrated"
      , "worried"
      , "depressed"
      , "struggling"
      , "falling behind"
      , "can't cope"
      , "too much"
    ];

    public async IAsyncEnumerable<Insight> GenerateAsync( ConversationContext context
                                                         , IObjectStore        store
                                                         , [EnumeratorCancellation]
                                                           CancellationToken   ct = default )
    {
        await Task.CompletedTask; // no async I/O needed in Phase A

        var message = context.LastUserMessage;
        if (string.IsNullOrWhiteSpace(message))
            yield break;

        var lower = message.ToLowerInvariant();
        var hasStressSignal = StressKeywords.Any(keyword => lower.Contains(keyword));

        if (!hasStressSignal)
            yield break;

        yield return new Insight
                     {
                             Message          = "It sounds like you have a lot going on. Want to log how you're feeling as a journal entry?"
                           , SuggestedAction  = "AddJournalEntry"
                           , Priority         = InsightPriority.Normal
                           , Category         = InsightCategory.Reflection
                           , DeduplicationKey = $"reflection.stress-detected.{context.SessionId}"
                           , Reasoning        = new InsightReasoning
                                                {
                                                    Explanation = "Stress or emotion language detected in the user's message."
                                                }
                     };
    }
}
