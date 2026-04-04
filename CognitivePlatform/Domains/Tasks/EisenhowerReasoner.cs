using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CognitivePlatform.Api.Avails.Extensions;

namespace CognitivePlatform.Api.Domains.Tasks;

public class EisenhowerReasoner
{
    public EisenhowerResult Analyze(IEnumerable<TaskItem> tasks)
    {
        var now = DateTimeOffset.UtcNow;

        var doItNew     = new List<TaskItem>(); // Do It Now / First
        var decide      = new List<TaskItem>(); // Decide / Schedule
        var doDeletgate = new List<TaskItem>(); // Delegate
        var delete      = new List<TaskItem>(); // Delete / Eliminate

        foreach (var taskItem in tasks.Where(taskItem => taskItem.IsDeleted
                                                                 .Not()))
        {
            switch (taskItem.IsImportant, taskItem.IsUrgent)
            {
                case (true, true):
                    doItNew.Add(taskItem);
                    break;
                
                case (true, false):
                    decide.Add(taskItem); 
                    break;
                
                case (false, true):
                    doDeletgate.Add(taskItem); 
                    break;
                
                default:
                    delete.Add(taskItem); break;
            }
        }

        return new EisenhowerResult(DoItNow:  Sort(doItNew)
                                  , Decide:   Sort(decide)
                                  , Delegate: Sort(doDeletgate)
                                  , Delete:   Sort(delete));
    }

    private static List<TaskItem> Sort(List<TaskItem> items)
    {
        return items.OrderBy(t => t.DueDate ?? DateTimeOffset.MaxValue)
                    .ThenBy(t => t.CreatedAt)
                    .ToList();
    }
}

public record EisenhowerResult
(
     IReadOnlyList<TaskItem> DoItNow
   , IReadOnlyList<TaskItem> Decide
   , IReadOnlyList<TaskItem> Delegate
   , IReadOnlyList<TaskItem> Delete
)
{
    public string ToHumanReadableString()
    {
        var sb = new StringBuilder();

        void AddSection(string title, IEnumerable<TaskItem> list)
        {
            sb.AppendLine()
              .AppendLine($"=== {title} ===");

            var taskItems = list as TaskItem[] ?? list.ToArray();
            if (taskItems.Length == 0)
            {
                sb.AppendLine("  (none)");
                return;
            }

            foreach (var taskItem in taskItems)
            {
                var due = taskItem.DueDate.HasValue
                                  ? $" (due {taskItem.DueDate:yyyy-MM-dd})"
                                  : string.Empty;

                sb.AppendLine($"- {taskItem.ShortDescription}{due}{Environment.NewLine}  [ID: {taskItem.Id}]");
            }
        }

        AddSection("Do It Now (Important & Urgent)",        DoItNow);
        AddSection("Decide (Important & Not Urgent)",       Decide);
        AddSection("Delegate (Not Important & Urgent)",     Delegate);
        AddSection("Delete (Not Important & Not Urgent)",   Delete);

        return sb.ToString();
    }
}
