using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CognitivePlatform.Api.Attributes;
using CognitivePlatform.Api.Avails.Extensions;

namespace CognitivePlatform.Api.Domains.Tasks;

public class TaskActions
{
    // Simple shared in-memory store for this phase.
    //private static readonly TaskRepositoryInMemory _repository = new();
    
    // Domain service (DI-friendly, even though ExecutionEngine currently
    // creates instances via Activator.CreateInstance)
    private readonly ITaskService _taskService;

    public TaskActions(ITaskService taskService)
    {
        _taskService = taskService;
    }
    
    [FastPath]
    [NaturalLanguageAction(    Description = "Create a new task with optional due date, priority, and tags."
                               , Examples = new[]
                                            {
                                                    "Create a task to buy groceries tomorrow with the personal tag."
                                                    , "Add an urgent work task to finish the report."
                                            }
                               , AllowsClarification = true)]
    public string AddTask ([NaturalLanguageParam(    Description = "Short description of the task."
                                                     , Optional = false
                                                     , AllowEmpty = false)]
                           string title
                           , [NaturalLanguageParam(    Description = "Additional details about the task."
                                                       , Optional = true)]
                           string? description = null
                           , [NaturalLanguageParam(    Description = "Whether this task is important."
                                                       , Optional = true
                                                       , DefaultValue = true)]
                           bool? isImportant = null
                           , [NaturalLanguageParam(
                                   Description = "Whether this task is urgent."
                                   , Optional = true
                                   , DefaultValue = false)]
                           bool? isUrgent = null
                           , [NaturalLanguageParam(    Description = "Due date or time for the task, if any."
                                                       , Optional = true
                                                       , AllowEmpty = true)]
                           string? dueDateText = null
                           , [NaturalLanguageParam(    Description = "Comma-separated list of tags."
                                                       , Optional = true
                                                       , AllowEmpty = true)]
                           string? tags = null)
    {
        var task = new TaskItem
                   {
                           Title = title
                           , Description = string.IsNullOrWhiteSpace(description)
                                                   ? null
                                                   : description.Trim()
                           , IsImportant = isImportant ?? true
                           , IsUrgent    = isUrgent    ?? false
                           , Tags        = ParseTags(tags)
                   };

        if (TryParseDate(dueDateText
                         , out var due))
        {
            task.DueDate = due;
        }

        _taskService.Save(task);

        return $"Task created with id {task.Id}:{Environment.NewLine}- {task.Title}";
    }

    [FastPath]
    [NaturalLanguageAction(    Description = "List tasks, optionally filtered by completion, urgency, importance, or tag."
                               , Examples = new[]
                                            {
                                                    "Show my tasks."
                                                    , "List urgent and important tasks."
                                                    , "Show my personal tasks that are not completed."
                                            }
                               , AllowsClarification = false)]
    public string ListTasks ([NaturalLanguageParam(    Description = "Include completed tasks (true/false)."
                                                       , Optional = true
                                                       , DefaultValue = false)]
                             bool? includeCompleted = null
                             , [NaturalLanguageParam(
                                     Description = "Only show urgent tasks."
                                     , Optional = true
                                     , DefaultValue = null)]
                             bool? onlyUrgent = null
                             , [NaturalLanguageParam(    Description = "Only show important tasks."
                                                         , Optional = true
                                                         , DefaultValue = null)]
                             bool? onlyImportant = null
                             , [NaturalLanguageParam(    Description = "Filter by a single tag."
                                                         , Optional = true
                                                         , AllowEmpty = true)]
                             string? tag = null)
    {
        var tasks = _taskService.GetAll()
                               .Where(item => item.IsDeleted.Not());

        if ((includeCompleted ?? false).Not())
        {
            tasks = tasks.Where(item => item.CompletedAt is null);
        }

        if (onlyUrgent.HasValue
         && onlyUrgent.Value)
        {
            tasks = tasks.Where(item => item.IsUrgent);
        }

        if (onlyImportant.HasValue
         && onlyImportant.Value)
        {
            tasks = tasks.Where(item => item.IsImportant);
        }

        if (tag.HasValue())
        {
            var normalized = tag.Trim();

            tasks = tasks.Where(item => item.Tags.Any(aTag => string.Equals(aTag
                                                                            , normalized
                                                                            , StringComparison.OrdinalIgnoreCase)));
        }

        var list = tasks.ToList();

        if (list.Count == 0)
        {
            return "No matching tasks found.";
        }

        var sb = new StringBuilder();

        sb.AppendLine($"Found {list.Count} task(s):");

        foreach (var task in list.OrderBy(t => t.DueDate ?? t.CreatedAt))
        {
            var status = task.CompletedAt is null
                                 ? "Open"
                                 : "Completed";
            var matrix = DescribeMatrix(task);
            var tagsPart = task.Tags.Count > 0
                                   ? $" [tags: {string.Join(", ", task.Tags)}]"
                                   : string.Empty;
            var duePart = task.DueDate.HasValue
                                  ? $" (due {task.DueDate:yyyy-MM-dd})"
                                  : string.Empty;

            sb.AppendLine($"- {task.Id}: {task.Title}{duePart} [{status}, {matrix}]{tagsPart}");
        }

        return sb.ToString();
    }

    [FastPath]
    [NaturalLanguageAction(    Description = "Mark a task as completed by its id."
                               , Examples = new[]
                                            {
                                                    "Mark task 1234 as done."
                                                    , "Complete the task with id abc."
                                            }
                               , AllowsClarification = true)]
    public string CompleteTask ([NaturalLanguageParam(    Description = "The id of the task to complete."
                                                          , Optional = false
                                                          , AllowEmpty = false)]
                                string id)
    {
        id = NormalizeId(id);
        
        var task = _taskService.GetById(id);

        if (task is null
         || task.IsDeleted)
        {
            return $"No active task found with id '{id}'.";
        }

        if (task.CompletedAt is not null)
        {
            return $"Task '{id}' is already completed.";
        }

        task.CompletedAt = DateTimeOffset.UtcNow;
        _taskService.Save(task);

        return $"Task '{id}' marked as completed.";
    }

    [FastPath]
    [NaturalLanguageAction(    Description = "Soft delete a task by its id."
                               , Examples = new[]
                                            {
                                                    "Delete task 1234."
                                                    , "Remove the task with id abc from my list."
                                            }
                               , AllowsClarification = true)]
    public string DeleteTask ([NaturalLanguageParam(    Description = "The id of the task to delete."
                                                        , Optional = false
                                                        , AllowEmpty = false)]
                              string id)
    {
        id = NormalizeId(id);
        
        var task = _taskService.GetById(id);

        if (task is null
         || task.IsDeleted)
        {
            return $"No active task found with id '{id}'.";
        }

        task.IsDeleted = true;
        _taskService.Save(task);

        return $"Task '{id}' has been deleted (soft delete).";
    }

    [FastPath]
    [NaturalLanguageAction(Description = "Analyzes all tasks using the Eisenhower Matrix and provides recommendations."
                         , Examples = new[]
                                      {
                                              "Prioritize my tasks."
                                            , "Analyze my workload."
                                            , "Which tasks should I do first?"
                                            , "Show my Eisenhower matrix."
                                      }
                         , AllowsClarification = false)]
    public string AnalyzeTasks()
    {
        var tasks    = _taskService.GetAll();
        var reasoner = new EisenhowerReasoner();
        var result   = reasoner.Analyze(tasks);

        return result.ToHumanReadableString();
    }

    // --- Helper methods -----------------------------------------------------

    private static bool TryParseDate (string?              text
                                      , out DateTimeOffset value)
    {
        if (text.HasValue()
         && DateTimeOffset.TryParse(text, out value))
        {
            return true;
        }

        value = default;
        return false;
    }

    private static List<string> ParseTags (string? value)
    {
        if (value.DoesNotHaveValueOrIsNullOrEmpty()) return new List<string>();

        return value.Split(',')
                    .Select(tag => tag.Trim())
                    .Where(tag => tag.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
    }

    private static string DescribeMatrix (TaskItem task)
    {
        return (task.IsImportant, task.IsUrgent) switch
        {
                  (true, true)  => "Do It Now (Important & Urgent)"
                , (true, false) => "Decide (Important & Not Urgent)"
                , (false, true) => "Delegate (Not Important & Urgent)"
                , _             => "Delete (Not Important & Not Urgent)"
        };
    }
    
    private static string NormalizeId(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return id;

        return id.Replace("{", "")
                 .Replace("}", "")
                 .Trim();
    }

}
