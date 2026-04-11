using System.Text;

namespace CognitivePlatform.Api.Domains.Tasks;

/// <summary>
/// Pre-formats a daily brief from two independent data slices:
///
///   1. Do It Now — tasks that are both Important and Urgent (Eisenhower Q1).
///   2. Due Today or Overdue — any active task whose due date is today or earlier.
///
/// These two sections are intentionally kept separate. A task can appear in both
/// (e.g. an Important+Urgent task due today), which is a useful signal to the user.
///
/// Calendar section is planned as a follow-on once calendar integration exists.
/// See DEFERRED.md — Phase 5: Calendar in DailyBrief.
/// </summary>
public class DailyBriefService : IDailyBriefService
{
    private readonly ITaskService       _taskService;
    private readonly EisenhowerReasoner _eisenhower = new();

    public DailyBriefService(ITaskService taskService)
    {
        _taskService = taskService ?? throw new ArgumentNullException(nameof(taskService));
    }

    public string GetBrief()
    {
        var today  = DateTimeOffset.UtcNow.Date;
        var active = _taskService.GetActive();

        var eisenhower = _eisenhower.Analyze(active);

        var dueToday = active
            .Where(t => t.DueDate.HasValue && t.DueDate.Value.UtcDateTime.Date <= today)
            .OrderBy(t => t.DueDate)
            .ThenBy(t => t.CreatedAt)
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"=== Daily Brief — {DateTimeOffset.UtcNow:yyyy-MM-dd} ===");

        // ---- Do It Now ----
        sb.AppendLine();
        sb.AppendLine("--- Do It Now (Important & Urgent) ---");

        if (eisenhower.DoItNow.Count == 0)
        {
            sb.AppendLine("  (none)");
        }
        else
        {
            foreach (var task in eisenhower.DoItNow)
            {
                var due = task.DueDate.HasValue
                                  ? $" (due {task.DueDate:yyyy-MM-dd})"
                                  : string.Empty;
                sb.AppendLine($"• {task.ShortDescription}{due}");
            }
        }

        // ---- Due Today / Overdue ----
        sb.AppendLine();
        sb.AppendLine("--- Due Today or Overdue ---");

        if (dueToday.Count == 0)
        {
            sb.AppendLine("  (none)");
        }
        else
        {
            foreach (var task in dueToday)
            {
                var isOverdue = task.DueDate!.Value.UtcDateTime.Date < today;
                var label     = isOverdue ? " [OVERDUE]" : string.Empty;
                sb.AppendLine($"• {task.ShortDescription} (due {task.DueDate:yyyy-MM-dd}){label}");
            }
        }

        return sb.ToString().TrimEnd();
    }
}
