using System.Runtime.CompilerServices;
using CognitivePlatform.Api.Conversation;
using CognitivePlatform.Api.Domains.Journal.Interfaces;
using CognitivePlatform.Api.Insights.Models;

namespace CognitivePlatform.Api.Insights;

/// <summary>
/// Phase E coaching provider. Fires when the user has logged stress-signal words
/// in journal entries on 3 or more distinct days within the last 7 days.
///
/// <para>
/// Trigger:          3+ distinct local days in the last 7 contain stress-signal words.
/// Message:          "You've logged stress signals {n} days this week. Want to explore what's been driving that?"
/// SuggestedAction:  AddJournalEntry
/// DeduplicationKey: coaching.stress-pattern-weekly
/// RepeatWindow:     72 hours (enforced by InsightPolicy via the engine).
/// InsightCategory:  Habit
/// </para>
/// </summary>
public sealed class StressPatternInsightProvider : IInsightProvider
{
    private static readonly IReadOnlyList<string> StressSignals =
    [
        "stressed"
      , "overwhelmed"
      , "anxious"
      , "tired"
      , "exhausted"
      , "burned out"
      , "burnout"
      , "struggling"
      , "drained"
      , "frustrated"
    ];

    private const int StressDayThreshold = 3;

    private readonly IJournalService _journalService;

    public InsightCategory Category => InsightCategory.Habit;

    public StressPatternInsightProvider(IJournalService journalService)
    {
        _journalService = journalService ?? throw new ArgumentNullException(nameof(journalService));
    }

    public async IAsyncEnumerable<Insight> GenerateAsync(
        ConversationContext                        context
      , [EnumeratorCancellation] CancellationToken cancellationToken = default )
    {
        cancellationToken.ThrowIfCancellationRequested();

        await Task.CompletedTask;

        var sevenDaysAgoUtc = DateTimeOffset.UtcNow.AddDays(-7);
        var entries         = _journalService.ListEntries(fromUtc: sevenDaysAgoUtc);

        var stressfulDays = entries
            .Where(entry => ContainsStressSignal(entry.LatestRevision.Text))
            .Select(entry => entry.Entry.CreatedUtc.LocalDateTime.Date)
            .Distinct()
            .Count();

        if (stressfulDays < StressDayThreshold)
            yield break;

        yield return new Insight
                     {
                             Message          = $"You've logged stress signals {stressfulDays} days this week. Want to explore what's been driving that?"
                           , SuggestedAction  = "AddJournalEntry"
                           , DeduplicationKey = "coaching.stress-pattern-weekly"
                           , Category         = InsightCategory.Habit
                           , Priority         = InsightPriority.Normal
                           , Reasoning        = new InsightReasoning
                                               {
                                                       Explanation = $"Stress signals detected in journal entries on {stressfulDays} of the last 7 days."
                                               }
                     };
    }

    private static bool ContainsStressSignal(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var lower = text.ToLowerInvariant();
        return StressSignals.Any(signal => lower.Contains(signal));
    }
}
