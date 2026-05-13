using System.Runtime.CompilerServices;
using CognitivePlatform.Api.Conversation;
using CognitivePlatform.Api.Domains.Journal.Interfaces;
using CognitivePlatform.Api.Insights.Models;

namespace CognitivePlatform.Api.Insights;

/// <summary>
/// Phase B insight provider. Fires when the user has not journaled today
/// (local calendar date), prompting them to add an entry.
///
/// <para>
/// Trigger:          No journal entry recorded for the current local date.
/// Message:          "You haven't journaled today — want to add an entry?"
/// SuggestedAction:  AddJournalEntry
/// DeduplicationKey: journal.no-entry-today
/// RepeatWindow:     24 hours (enforced by <see cref="InsightPolicy"/> via the engine).
/// </para>
/// </summary>
public sealed class JournalActivityInsightProvider : IInsightProvider
{
    private readonly IJournalService _journalService;

    public InsightCategory Category => InsightCategory.Journal;

    public JournalActivityInsightProvider(IJournalService journalService)
    {
        _journalService = journalService ?? throw new ArgumentNullException(nameof(journalService));
    }

    public async IAsyncEnumerable<Insight> GenerateAsync(
        ConversationContext                        context
      , [EnumeratorCancellation] CancellationToken cancellationToken = default )
    {
        cancellationToken.ThrowIfCancellationRequested();

        // No async I/O needed — journal service is synchronous. The await below
        // satisfies the async-iterator contract without adding real latency.
        await Task.CompletedTask;

        // Query only today's local-date window so we don't load the entire history.
        var today   = DateTime.Now.Date;
        var fromUtc = new DateTimeOffset(today,            TimeZoneInfo.Local.GetUtcOffset(today));
        var toUtc   = new DateTimeOffset(today.AddDays(1), TimeZoneInfo.Local.GetUtcOffset(today.AddDays(1)));

        var todaysEntries = _journalService.ListEntries(fromUtc: fromUtc, toUtc: toUtc);

        if (todaysEntries.Count > 0)
            yield break;

        yield return new Insight
                     {
                             Message          = "You haven't journaled today — want to add an entry?"
                           , SuggestedAction  = "AddJournalEntry"
                           , DeduplicationKey = "journal.no-entry-today"
                           , Category         = InsightCategory.Journal
                           , Priority         = InsightPriority.Normal
                     };
    }
}
