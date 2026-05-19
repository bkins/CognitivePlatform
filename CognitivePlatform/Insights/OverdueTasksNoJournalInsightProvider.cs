using System.Runtime.CompilerServices;
using CognitivePlatform.Api.Conversation;
using CognitivePlatform.Api.Domains.Journal.Interfaces;
using CognitivePlatform.Api.Domains.Tasks;
using CognitivePlatform.Api.Insights.Models;

namespace CognitivePlatform.Api.Insights;

/// <summary>
/// Phase E coaching provider. Fires when the user has at least 2 tasks past
/// their due date and has not written a journal entry today.
///
/// <para>
/// Trigger:          Active task count with DueDate &lt; UtcNow >= 2 AND no journal entry today.
/// Message:          "You have {n} overdue tasks and haven't journaled today. It might help to write down what's blocking you."
/// SuggestedAction:  AddJournalEntry
/// DeduplicationKey: coaching.overdue-no-journal
/// RepeatWindow:     48 hours (enforced by InsightPolicy via the engine).
/// InsightCategory:  Tasks
/// </para>
/// </summary>
public sealed class OverdueTasksNoJournalInsightProvider : IInsightProvider
{
    private const int OverdueTaskThreshold = 2;

    private readonly ITaskService    _taskService;
    private readonly IJournalService _journalService;

    public InsightCategory Category => InsightCategory.Tasks;

    public OverdueTasksNoJournalInsightProvider( ITaskService    taskService
                                               , IJournalService journalService )
    {
        _taskService    = taskService    ?? throw new ArgumentNullException(nameof(taskService));
        _journalService = journalService ?? throw new ArgumentNullException(nameof(journalService));
    }

    public async IAsyncEnumerable<Insight> GenerateAsync(
        ConversationContext                        context
      , [EnumeratorCancellation] CancellationToken cancellationToken = default )
    {
        cancellationToken.ThrowIfCancellationRequested();

        await Task.CompletedTask;

        var now          = DateTimeOffset.UtcNow;
        var overdueTasks = _taskService.GetActive()
                                       .Where(task => task.DueDate.HasValue && task.DueDate.Value < now)
                                       .ToList();

        if (overdueTasks.Count < OverdueTaskThreshold)
            yield break;

        var today   = DateTime.Now.Date;
        var fromUtc = new DateTimeOffset(today,            TimeZoneInfo.Local.GetUtcOffset(today))           .ToUniversalTime();
        var toUtc   = new DateTimeOffset(today.AddDays(1), TimeZoneInfo.Local.GetUtcOffset(today.AddDays(1))).ToUniversalTime();

        var todaysEntries = _journalService.ListEntries(fromUtc: fromUtc, toUtc: toUtc);

        if (todaysEntries.Count > 0)
            yield break;

        yield return new Insight
                     {
                             Message          = $"You have {overdueTasks.Count} overdue tasks and haven't journaled today. It might help to write down what's blocking you."
                           , SuggestedAction  = "AddJournalEntry"
                           , DeduplicationKey = "coaching.overdue-no-journal"
                           , Category         = InsightCategory.Tasks
                           , Priority         = InsightPriority.Normal
                           , Reasoning        = new InsightReasoning
                                               {
                                                       Explanation = $"You have {overdueTasks.Count} overdue tasks and no journal entry today."
                                               }
                     };
    }
}
