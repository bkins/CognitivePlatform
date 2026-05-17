using System.Runtime.CompilerServices;
using CognitivePlatform.Api.Conversation;
using CognitivePlatform.Api.Domains.Tasks;
using CognitivePlatform.Api.Insights.Models;

namespace CognitivePlatform.Api.Insights;

/// <summary>
/// Phase C insight provider. Fires when the user has more than
/// <see cref="OpenTaskThreshold"/> open (non-completed, non-deleted) tasks,
/// prompting them to prioritise their list.
///
/// <para>
/// Trigger:          Open task count &gt; OpenTaskThreshold (3).
/// Message:          "You have {n} open tasks. Want help prioritising?"
/// SuggestedAction:  ListTasksSummary
/// DeduplicationKey: tasks.open-tasks-reminder
/// RepeatWindow:     48 hours (enforced by <see cref="InsightPolicy"/> via the engine).
/// </para>
/// </summary>
public sealed class TaskAwarenessInsightProvider : IInsightProvider
{
    private const int OpenTaskThreshold = 3;

    private readonly ITaskService _taskService;

    public InsightCategory Category => InsightCategory.Tasks;

    public TaskAwarenessInsightProvider(ITaskService taskService)
    {
        _taskService = taskService ?? throw new ArgumentNullException(nameof(taskService));
    }

    public async IAsyncEnumerable<Insight> GenerateAsync(
        ConversationContext                        context
      , [EnumeratorCancellation] CancellationToken cancellationToken = default )
    {
        cancellationToken.ThrowIfCancellationRequested();

        // No async I/O needed — task service is synchronous.
        await Task.CompletedTask;

        var openTasks = _taskService.GetActive();

        if (openTasks.Count <= OpenTaskThreshold)
            yield break;

        yield return new Insight
                     {
                             Message          = $"You have {openTasks.Count} open tasks. Want help prioritising?"
                           , SuggestedAction  = "ListTasksSummary"
                           , DeduplicationKey = "tasks.open-tasks-reminder"
                           , Category         = InsightCategory.Tasks
                           , Priority         = InsightPriority.Normal
                     };
    }
}
