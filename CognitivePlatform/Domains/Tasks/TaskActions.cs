using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using CognitivePlatform.Api.Attributes;
using CP.Shared.Primitives.Avails.Extensions;

namespace CognitivePlatform.Api.Domains.Tasks;

public class TaskActions
{
    private readonly ITaskService       _taskService;
    private readonly IDailyBriefService _dailyBrief;

    public TaskActions( ITaskService       taskService
                      , IDailyBriefService dailyBrief )
    {
        _taskService = taskService ?? throw new ArgumentNullException(nameof(taskService));
        _dailyBrief  = dailyBrief  ?? throw new ArgumentNullException(nameof(dailyBrief));
    }

    [FastPath]
    [NaturalLanguageAction(Description = "Create a new task with optional due date, priority, and tags."
                         , Examples =
                           [
                                   "Create a task to buy groceries tomorrow with the personal tag."
                                 , "Add an urgent work task to finish the report."
                                 , "Add task buy milk"
                                 , "Remind me to call mom tonight"
                                 , "Create a task to finish report by Friday"
                           ]
                         , AllowsClarification = true, IsReplayable = true)]
    public string AddTask( [NaturalLanguageParam(Description = "Short description of the task."
                                               , Optional    = false
                                               , AllowEmpty  = false)]
                           string shortDescription
                         , [NaturalLanguageParam(Description = "Additional details about the task."
                                               , Optional    = true)]
                           string? details = null
                         , [NaturalLanguageParam(Description  = "Whether this task is important."
                                               , Optional     = true
                                               , DefaultValue = true)]
                           bool? isImportant = null
                         , [NaturalLanguageParam(Description  = "Whether this task is urgent."
                                               , Optional     = true
                                               , DefaultValue = false)]
                           bool? isUrgent = null
                         , [NaturalLanguageParam(Description = "Due date or time for the task, if any."
                                               , Optional    = true
                                               , AllowEmpty  = true)]
                           string? dueDateText = null
                         , [NaturalLanguageParam(Description = "Comma-separated list of tags."
                                               , Optional    = true
                                               , AllowEmpty  = true)]
                           string? tags = null
                         , [NaturalLanguageParam(Description = "The task's priority: low, normal, high, or critical."
                                               , Optional    = true)]
                           TaskPriority priority = TaskPriority.Normal )
    {
        var task = new TaskItem
                   {
                           ShortDescription = shortDescription
                         , Details          = string.IsNullOrWhiteSpace(details) ? null : details.Trim()
                         , IsImportant      = isImportant ?? true
                         , IsUrgent         = isUrgent    ?? false
                         , Tags             = ParseTags(tags)
                         , Priority         = priority
                         , CreatedAt        = DateTimeOffset.UtcNow
                         , UpdatedAt        = DateTimeOffset.UtcNow
                   };

        if (TryParseDate(dueDateText, out var due))
            task.DueDate = due;

        _taskService.Create(task);

        return $"Task created: {task.ShortDescription}";
    }

    [FastPath]
    [NaturalLanguageAction(Description = "Create multiple tasks at once from a list. Use when the user provides several tasks in a single message."
                         , Examples =
                           [
                                   "I need to do several things: review Elena's PD, check the CRM servers, and look at bug 161843."
                                 , "Add these tasks: buy milk, call dentist, finish report by Friday."
                                 , "Let me give you a list of tasks I need to track."
                                 , "I have a few things I need to get done today."
                           ]
                         , AllowsClarification = false)]
    public string BatchAddTasks( [NaturalLanguageParam(Description = "A pipe-separated list of task descriptions extracted from the user's message. "
                                                                   + "Each entry is the short description of one task. "
                                                                   + "Example: 'Review Elena PD|Check CRM servers|Look at bug 161843'"
                                                     , Optional    = false
                                                     , AllowEmpty  = false)]
                                 string descriptions )
    {
        var parsed = descriptions.Split('|')
                                 .Select(description => description.Trim())
                                 .Where(description  => description.Length > 0)
                                 .Distinct(StringComparer.OrdinalIgnoreCase)
                                 .Select(description => new TaskItem { ShortDescription = description })
                                 .ToList();

        if (parsed.Count == 0)
            return "No task descriptions were found in your message. Please list the tasks you want to add.";

        var created = _taskService.CreateBatch(parsed);

        // Re-query the full-ordered list after creation so position numbers in the
        // response match what a subsequent ListTasks call would show. The user can
        // immediately say "task 2 is done" without a separate ListTasks call.
        var allOrdered = _taskService.GetOrderedActiveTasks();
        var createdIds = created.Select(task => task.Id).ToHashSet();

        var sb = new StringBuilder();
        sb.AppendLine($"Created {created.Count} task(s). Here is your current task list:");
        sb.AppendLine();

        foreach (var (position, task) in allOrdered)
        {
            var marker  = createdIds.Contains(task.Id) ? "NEW" : "   ";
            var duePart = task.DueDate.HasValue ? $"  Due: {task.DueDate:yyyy-MM-dd}" : string.Empty;

            sb.AppendLine($"{position} - [{marker}] {task.ShortDescription}{duePart}");
        }

        return sb.ToString();
    }

    [FastPath]
    [NaturalLanguageAction(Description = "List tasks, optionally filtered by completion, urgency, importance, tag, or due-date window."
                         , Examples =
                           [
                                   "Show my tasks."
                                 , "List urgent and important tasks."
                                 , "Show my personal tasks that are not completed."
                                 , "Show high priority tasks"
                                 , "Show overdue tasks"
                                 , "Show tasks due tomorrow"
                                 , "Show tasks due this week"
                                 , "Tasks due last week"
                                 , "What tasks are due next week?"
                           ]
                         , AllowsClarification = false)]
    public string ListTasks( [NaturalLanguageParam(Description  = "Include completed tasks (true/false)."
                                                 , Optional     = true
                                                 , DefaultValue = false)]
                             bool? includeCompleted = null
                           , [NaturalLanguageParam(Description  = "Only show urgent tasks."
                                                 , Optional     = true
                                                 , DefaultValue = null)]
                             bool? onlyUrgent = null
                           , [NaturalLanguageParam(Description  = "Only show important tasks."
                                                 , Optional     = true
                                                 , DefaultValue = null)]
                             bool? onlyImportant = null
                           , [NaturalLanguageParam(Description = "Filter by a single tag."
                                                 , Optional    = true
                                                 , AllowEmpty  = true)]
                             string? tag = null
                           , [NaturalLanguageParam(Description  = "Filter tasks due before this date."
                                                 , Optional     = true
                                                 , DefaultValue = null)]
                             DateTimeOffset? dueBefore = null
                           , [NaturalLanguageParam(Description  = "Filter tasks due after this date."
                                                 , Optional     = true
                                                 , DefaultValue = null)]
                             DateTimeOffset? dueAfter = null
                           , [NaturalLanguageParam(Description  = "Filter to past-due tasks only."
                                                 , Optional     = true
                                                 , DefaultValue = false)]
                             bool overdueOnly = false )
    {
        IEnumerable<TaskItem> tasks = _taskService.QueryTasks(includeCompleted
                                                            , onlyUrgent
                                                            , onlyImportant
                                                            , tag);

        tasks = FilterByDueDate(dueBefore, dueAfter, overdueOnly, tasks);

        // Preserve stable position numbers from the full ordered list even when
        // filters are active, so "task 3" always refers to the same task regardless
        // of which filtered view the user is looking at.
        var allOrdered  = _taskService.GetOrderedActiveTasks();
        var filteredIds = tasks.Select(taskItem => taskItem.Id).ToHashSet();
        var list        = allOrdered.Where(entry => filteredIds.Contains(entry.Task.Id)).ToList();

        if (list.Count == 0)
            return "No matching tasks found.";

        var sb = new StringBuilder();

        sb.AppendLine($"# Tasks ({list.Count})");
        sb.AppendLine();

        foreach (var (position, task) in list)
        {
            var shortDescription = task.ShortDescription.HasNoValue()
                                           ? "_No description_"
                                           : task.ShortDescription;

            var status = task.CompletedAt is null ? "Open" : "Completed";
            var matrix = DescribeMatrix(task);
            var tagsPart = task.Tags.Count > 0
                                   ? string.Join(", ", task.Tags)
                                   : "_None_";
            var duePart  = task.DueDate.HasValue
                                   ? task.DueDate.Value.ToString("yyyy-MM-dd")
                                   : "_None_";

            sb.AppendLine($"## {position}. {shortDescription}");
            sb.AppendLine();

            sb.AppendLine($"- **Status:** {status}");
            sb.AppendLine($"- **Priority:** {matrix}");
            sb.AppendLine($"- **Due:** {duePart}");
            sb.AppendLine($"- **Tags:** {tagsPart}");

            sb.AppendLine();
        }

        return sb.ToString();

    }

    [FastPath]
    [NaturalLanguageAction(Description = "List completed tasks, most recently completed first."
                         , Examples =
                           [
                                   "Show my completed tasks."
                                 , "What tasks have I finished?"
                                 , "List my done tasks."
                                 , "Show tasks I've completed."
                                 , "What have I accomplished?"
                           ]
                         , AllowsClarification = false)]
    public string ListCompletedTasks()
    {
        var completed = _taskService.GetCompleted();

        if (completed.Count == 0)
            return "No completed tasks found.";

        var sb = new StringBuilder();
        sb.AppendLine($"# Completed Tasks ({completed.Count})");
        sb.AppendLine();

        foreach (var task in completed)
        {
            var shortDescription = task.ShortDescription.HasNoValue()
                                           ? "_No description_"
                                           : task.ShortDescription;

            var completedPart = task.CompletedAt?.ToString("yyyy-MM-dd") ?? "_Unknown_";
            var tagsPart      = task.Tags.Count > 0
                                        ? string.Join(", ", task.Tags)
                                        : "_None_";

            sb.AppendLine($"## {shortDescription}");
            sb.AppendLine();
            sb.AppendLine($"- **Completed:** {completedPart}");
            sb.AppendLine($"- **Tags:** {tagsPart}");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    [FastPath]
    [NaturalLanguageAction(Description = "Mark a task as completed. Accepts a task position number (from the list) or a full task id."
                         , Examples =
                           [
                                   "Mark task 2 as done."
                                 , "Complete task 1."
                                 , "Task 3 is done."
                                 , "Mark task id abc123 as complete."
                           ]
                         , AllowsClarification = true, IsReplayable = true)]
    public string CompleteTask( [NaturalLanguageParam(Description = "The position number of the task (e.g. '2') or its full id."
                                                    , Optional    = false
                                                    , AllowEmpty  = false)]
                                string taskReference )
    {
        var task = TryResolveTaskReference(taskReference, out var resolveError);

        if (task is null)
            return resolveError!;

        if (task.CompletedAt is not null)
            return $"Task '{task.ShortDescription}' is already completed (completed on: {task.CompletedAt:yyyy-MM-dd}).";

        var completedAt = _taskService.Complete(task.Id);

        return $"Task {taskReference} — '{task.ShortDescription}' marked as completed at {completedAt}.";
    }

    [FastPath]
    [NaturalLanguageAction(
            Description = "Mark multiple tasks as completed in one command. "
                        + "Accepts a mix of position numbers and task ids."
          , Examples =
            [
                    "Tasks 2 and 4 are done."
                  , "Mark tasks 1, 3, and 5 as complete."
                  , "I finished tasks 2 and 3."
                  , "Complete tasks 1 and 4."
            ]
          , AllowsClarification = false)]
    public string CompleteBatch( [NaturalLanguageParam(Description = "A pipe-separated list of task position numbers or ids to mark complete. "
                                                                   + "Example: '2|4' or '1|3|5'"
                                                     , Optional    = false
                                                     , AllowEmpty  = false)]
                                 string taskReferences )
    {
        // The system prompt instructs the LLM to separate references with '|'.
        // Previously this was ',' which caused "2|4" to be treated as a single
        // unresolvable reference, crashing on Guid.Parse.
        var references = taskReferences.Split('|')
                                       .Select(reference => reference.Trim())
                                       .Where(reference  => reference.Length > 0)
                                       .Distinct(StringComparer.OrdinalIgnoreCase)
                                       .ToList();

        if (references.Count == 0)
            return "No task references were found. Please provide position numbers or ids separated by pipes (e.g. '2|4').";

        var succeeded = new List<string>();
        var skipped   = new List<string>();
        var failed    = new List<string>();

        foreach (var reference in references)
        {
            var task = TryResolveTaskReference(reference, out var resolveError);

            if (task is null)
            {
                failed.Add($"  {reference}: {resolveError}");
                continue;
            }

            if (task.CompletedAt is not null)
            {
                skipped.Add($"  {reference} — '{task.ShortDescription}' (already completed)");
                continue;
            }

            _taskService.Complete(task.Id);

            succeeded.Add($"  {reference} — '{task.ShortDescription}'");
        }

        var sb = new StringBuilder();

        if (succeeded.Count > 0)
        {
            sb.AppendLine($"Completed {succeeded.Count} task(s):");
            succeeded.ForEach(line => sb.AppendLine(line));
        }

        if (skipped.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"Already completed ({skipped.Count}):");
            skipped.ForEach(line => sb.AppendLine(line));
        }

        if (failed.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"Could not resolve ({failed.Count}):");
            failed.ForEach(line => sb.AppendLine(line));
        }

        return sb.ToString().TrimEnd();
    }

    [FastPath]
    [NaturalLanguageAction(
            Description = "Update the priority, importance, or urgency of an existing task. "
                        + "Accepts a task position number or full task id. "
                        + "Provide at least one of: priority, isImportant, or isUrgent."
          , Examples =
            [
                    "Make task 1 high priority."
                  , "Task 3 is now urgent."
                  , "Mark task 2 as not important."
                  , "Set task 4 to critical priority."
                  , "Task 1 is important and urgent."
            ]
          , AllowsClarification = true)]
    public string UpdateTaskPriority( [NaturalLanguageParam(Description = "The position number of the task (e.g. '2') or its full id."
                                                          , Optional    = false
                                                          , AllowEmpty  = false)]
                                      string taskReference
                                    , [NaturalLanguageParam(Description = "New priority level: low, normal, high, or critical."
                                                          , Optional    = true)]
                                      TaskPriority? priority = null
                                    , [NaturalLanguageParam(Description = "Whether this task is important."
                                                          , Optional    = true)]
                                      bool? isImportant = null
                                    , [NaturalLanguageParam(Description = "Whether this task is urgent."
                                                          , Optional    = true)]
                                      bool? isUrgent = null )
    {
        if (priority is null && isImportant is null && isUrgent is null)
            return "Please specify at least one field to update: priority, isImportant, or isUrgent.";

        var task = TryResolveTaskReference(taskReference, out var resolveError);

        if (task is null)
            return resolveError!;

        var changes = new List<string>();

        if (priority is not null && priority != task.Priority)
        {
            changes.Add($"priority: {task.Priority} → {priority}");
            task.Priority = priority.Value;
        }

        if (isImportant is not null && isImportant != task.IsImportant)
        {
            changes.Add($"important: {task.IsImportant} → {isImportant}");
            task.IsImportant = isImportant.Value;
        }

        if (isUrgent is not null && isUrgent != task.IsUrgent)
        {
            changes.Add($"urgent: {task.IsUrgent} → {isUrgent}");
            task.IsUrgent = isUrgent.Value;
        }

        if (changes.Count == 0)
            return $"Task '{task.ShortDescription}' already has those values — no changes made.";

        _taskService.Update(task);

        var sb = new StringBuilder();
        sb.AppendLine($"Task {taskReference} — '{task.ShortDescription}' updated:");
        changes.ForEach(change => sb.AppendLine($"  {change}"));
        sb.AppendLine();
        sb.AppendLine($"Decision Matrix: {DescribeMatrix(task)}");

        return sb.ToString().TrimEnd();
    }

    [FastPath]
    [NaturalLanguageAction(
            Description = "Set or clear the due date on an existing task. "
                        + "Accepts a task position number or full task id. "
                        + "Pass an empty dueDateText to clear the due date."
          , Examples =
            [
                    "Task 2 is due Friday."
                  , "Set the due date on task 3 to April 5th."
                  , "Task 1 is due tomorrow."
                  , "Move task 4's due date to next Monday."
                  , "Clear the due date on task 2."
                  , "Task 3 has no due date."
            ]
          , AllowsClarification = true, IsReplayable = true)]
    public string UpdateTaskDueDate( [NaturalLanguageParam(Description = "The position number of the task (e.g. '2') or its full id."
                                                         , Optional    = false
                                                         , AllowEmpty  = false)]
                                     string taskReference
                                   , [NaturalLanguageParam(Description = "The new due date as natural text (e.g. 'Friday', 'April 5th', 'tomorrow'). "
                                                                       + "Pass an empty string to clear the existing due date."
                                                         , Optional    = false
                                                         , AllowEmpty  = true)]
                                     string dueDateText )
    {
        var task = TryResolveTaskReference(taskReference, out var resolveError);

        if (task is null)
            return resolveError!;

        var previousDue = task.DueDate;

        if (string.IsNullOrWhiteSpace(dueDateText))
        {
            if (task.DueDate is null)
                return $"Task '{task.ShortDescription}' already has no due date.";

            task.DueDate  = null;
            task.UpdatedAt = DateTimeOffset.UtcNow;
            _taskService.Update(task);

            return $"Task {taskReference} — '{task.ShortDescription}': due date cleared (was {previousDue:yyyy-MM-dd}).";
        }

        if (TryParseDate(dueDateText, out var parsed).Not())
            return $"Could not parse '{dueDateText}' as a date. Try a format like 'Friday', 'April 5th', or '2026-04-05'.";

        if (parsed == task.DueDate)
            return $"Task '{task.ShortDescription}' already has that due date ({parsed:yyyy-MM-dd}) — no changes made.";

        task.DueDate   = parsed;
        task.UpdatedAt = DateTimeOffset.UtcNow;
        _taskService.Update(task);

        var previousPart = previousDue.HasValue
                                   ? $" (was {previousDue:yyyy-MM-dd})"
                                   : " (previously none)";

        return $"Task {taskReference} — '{task.ShortDescription}': due date set to {parsed:yyyy-MM-dd}{previousPart}.";
    }

    [FastPath]
    [DestructiveAction]
    [NaturalLanguageAction(Description = "Soft delete a task. Accepts a task position number (from the list) or a full task id."
                         , Examples =
                           [
                                   "Delete task 2."
                                 , "Remove task 4 from my list."
                                 , "Delete the task with id abc123."
                           ]
                         , AllowsClarification = true, IsReplayable = true)]
    public string DeleteTask( [NaturalLanguageParam(Description = "The position number of the task (e.g. '2') or its full id."
                                                  , Optional    = false
                                                  , AllowEmpty  = false)]
                              string taskReference )
    {
        var task = TryResolveTaskReference(taskReference, out var resolveError);

        if (task is null)
            throw new InvalidOperationException(resolveError!);

        _taskService.Delete(task.Id);

        return $"Task {taskReference} — '{task.ShortDescription}' has been deleted.";
    }

    [FastPath]
    [NaturalLanguageAction(Description = "Analyzes all tasks using the Eisenhower Matrix and provides recommendations."
                         , Examples =
                           [
                                   "Prioritize my tasks."
                                 , "Analyze my workload."
                                 , "Which tasks should I do first?"
                                 , "Show my Eisenhower matrix."
                           ]
                         , AllowsClarification = false)]
    public string AnalyzeTasks()
    {
        var tasks    = _taskService.GetActive();
        var reasoner = new EisenhowerReasoner();
        var result   = reasoner.Analyze(tasks);

        return result.ToHumanReadableString();
    }

    [FastPath]
    [NaturalLanguageAction(Description = "Shows the daily brief: the most urgent tasks (Important & Urgent) and anything due today or overdue."
                         , Examples =
                           [
                                   "Give me my daily brief."
                                 , "What's on my plate today?"
                                 , "Show me today's priorities."
                                 , "Daily brief."
                                 , "What do I need to do today?"
                                 , "Morning briefing."
                           ]
                         , AllowsClarification = false)]
    public string GetDailyBrief()
    {
        // Pass today's local date to the brief service so the calendar window and
        // due-today filter key off the user's "today" rather than the server's UTC day.
        // Single-user/same-machine assumption holds today; when multi-client or multi-tz
        // becomes real, source this from ConversationContext metadata instead.
        return _dailyBrief.GetBrief(DateOnly.FromDateTime(DateTime.Now));
    }

    // --- Private helpers ----------------------------------------------------

    /// <summary>
    /// Resolves a taskReference that is either a 1-based position integer or a GUID string.
    /// Returns the resolved TaskItem, or null with an error message in <paramref name="errorMessage"/>.
    /// </summary>
    private TaskItem? TryResolveTaskReference( string      taskReference
                                             , out string? errorMessage )
    {
        taskReference = NormalizeId(taskReference);

        if (int.TryParse(taskReference, out var position))
        {
            var byPosition = _taskService.ResolveByPosition(position);

            if (byPosition is null || byPosition.IsDeleted)
            {
                errorMessage = $"No active task found at position {position}.";
                return null;
            }

            errorMessage = null;
            return byPosition;
        }

        var byId = _taskService.Get(taskReference);

        if (byId is null || byId.IsDeleted)
        {
            errorMessage = $"No active task found with id '{taskReference}'.";
            return null;
        }

        errorMessage = null;
        return byId;
    }

    private static IEnumerable<TaskItem> FilterByDueDate( DateTimeOffset?       dueBefore
                                                        , DateTimeOffset?       dueAfter
                                                        , bool                  overdueOnly
                                                        , IEnumerable<TaskItem> tasks )
    {
        if (overdueOnly)
        {
            return tasks.Where(taskItem => taskItem.DueDate     != null
                                        && taskItem.DueDate     < DateTimeOffset.UtcNow
                                        && taskItem.CompletedAt == null);
        }

        if (dueBefore is not null)
            tasks = tasks.Where(taskItem => taskItem.DueDate != null && taskItem.DueDate < dueBefore);

        if (dueAfter is not null)
            tasks = tasks.Where(taskItem => taskItem.DueDate != null && taskItem.DueDate > dueAfter);

        return tasks;
    }

    // ENH-05: natural language due-date parsing.
    // Handles relative expressions ("tomorrow", "next friday", "in 3 days") before
    // falling back to DateTimeOffset.TryParse for absolute dates ("April 5th", "2026-04-05").
    private static bool TryParseDate( string?           text
                                    , out DateTimeOffset value )
    {
        value = default;

        if (!text.HasValue()) return false;

        var now        = DateTimeOffset.UtcNow;
        var normalized = text!.Trim().ToLowerInvariant();

        // ── Simple keyword tokens ────────────────────────────────────────────
        switch (normalized)
        {
            case "today":
                value = now.Date;
                return true;

            case "tomorrow":
                value = now.Date.AddDays(1);
                return true;

            case "yesterday":
                value = now.Date.AddDays(-1);
                return true;

            case "next week":
                value = now.Date.AddDays(7);
                return true;

            case "end of week" or "this weekend" or "weekend":
                value = NextDayOfWeek(now.Date, DayOfWeek.Saturday);
                return true;

            case "end of month":
                value = new DateTimeOffset(now.Year
                                         , now.Month
                                         , DateTime.DaysInMonth(now.Year, now.Month)
                                         , 0, 0, 0
                                         , now.Offset);
                return true;
        }

        // ── "next <weekday>" or plain "<weekday>" ────────────────────────────
        var isNext    = normalized.StartsWith("next ");
        var dayToken  = isNext ? normalized["next ".Length..] : normalized;

        if (TryParseDayOfWeekToken(dayToken, out var dow))
        {
            // "next monday" always jumps to the coming Monday even if today IS Monday.
            // Plain "monday" snaps to the next occurrence (today counts if it is today).
            value = isNext
                        ? NextDayOfWeek(now.Date.AddDays(1), dow)
                        : NextDayOfWeek(now.Date, dow);
            return true;
        }

        // ── "in N days / weeks / months" ────────────────────────────────────
        var inMatch = Regex.Match(normalized, @"^in\s+(\d+)\s+(day|days|week|weeks|month|months)$");
        if (inMatch.Success)
        {
            var n    = int.Parse(inMatch.Groups[1].Value);
            var unit = inMatch.Groups[2].Value;

            value = unit.StartsWith("month")
                        ? now.Date.AddMonths(n)
                        : unit.StartsWith("week")
                            ? now.Date.AddDays(n * 7)
                            : now.Date.AddDays(n);
            return true;
        }

        // ── Absolute date strings ("April 5th", "2026-04-05", etc.) ─────────
        if (DateTimeOffset.TryParse(text, out value))
            return true;

        return false;
    }

    /// <summary>Returns the next occurrence of <paramref name="target"/> on or after <paramref name="from"/>.</summary>
    private static DateTimeOffset NextDayOfWeek( DateTimeOffset from
                                               , DayOfWeek     target )
    {
        var daysUntil = ((int)target - (int)from.DayOfWeek + 7) % 7;
        return from.Date.AddDays(daysUntil == 0 ? 7 : daysUntil);
    }

    private static bool TryParseDayOfWeekToken( string token, out DayOfWeek result )
    {
        switch (token)
        {
            case "monday"    or "mon": result = DayOfWeek.Monday;    return true;
            case "tuesday"   or "tue": result = DayOfWeek.Tuesday;   return true;
            case "wednesday" or "wed": result = DayOfWeek.Wednesday; return true;
            case "thursday"  or "thu": result = DayOfWeek.Thursday;  return true;
            case "friday"    or "fri": result = DayOfWeek.Friday;    return true;
            case "saturday"  or "sat": result = DayOfWeek.Saturday;  return true;
            case "sunday"    or "sun": result = DayOfWeek.Sunday;    return true;
            default:
                result = default;
                return false;
        }
    }

    private static HashSet<string> ParseTags(string? value)
    {
        if (value?.HasNoValue() ?? true) return [];

        return new HashSet<string>(value.Split(',')
                                        .Select(tag => tag.Trim())
                                        .Where(tag  => tag.Length > 0)
                                        .Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static string DescribeMatrix(TaskItem task)
    {
        return (task.IsImportant, task.IsUrgent) switch
        {
                (true,  true)  => "Do It Now (Important & Urgent)"
              , (true,  false) => "Decide (Important & Not Urgent)"
              , (false, true)  => "Delegate (Not Important & Urgent)"
              , _              => "Delete (Not Important & Not Urgent)"
        };
    }

    private static string NormalizeId(string id)
    {
        if (id.HasNoValue())
            return id;

        return id.Replace("{", "")
                 .Replace("}", "")
                 .Trim();
    }
}
