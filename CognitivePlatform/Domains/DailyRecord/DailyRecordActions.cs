using CognitivePlatform.Api.Attributes;
using CognitivePlatform.Api.Domains.Tasks;

namespace CognitivePlatform.Api.Domains.DailyRecord;

public class DailyRecordActions
{
    private readonly IDailyRecordService _dailyRecordService;
    private readonly ITaskService        _taskService;

    public DailyRecordActions( IDailyRecordService dailyRecordService
                              , ITaskService        taskService )
    {
        _dailyRecordService = dailyRecordService ?? throw new ArgumentNullException(nameof(dailyRecordService));
        _taskService        = taskService        ?? throw new ArgumentNullException(nameof(taskService));
    }

    // =========================================================================
    // OpenDay — morning plan
    // =========================================================================

    [FastPath]
    [NaturalLanguageAction(
          Description          = "Open today's daily plan. Call when the user submits their morning intention, priorities, or task list for the day."
        , Examples             =
          [
                  "Plan: Today I'm focusing on the refactor. Tasks: - Write unit tests - Review the PR"
                , "Plan: Quiet day, catching up on emails. Mood: Calm MoodScore: 3"
                , "Morning plan: deep work on the API, then team sync."
          ]
        , Category             = "daily"
        , AllowsClarification  = false)]
    public async Task<string> OpenDay(
          [NaturalLanguageParam(Description = "The opening text — the user's stated intention for the day."
                              , Optional    = false
                              , AllowEmpty  = false)]
          string openingText

        , [NaturalLanguageParam(Description = "Comma-separated task titles to add as today's planned tasks."
                              , Optional    = true
                              , AllowEmpty  = true)]
          string? tasks = null

        , [NaturalLanguageParam(Description = "The user's mood label, e.g. 'Focused'."
                              , Optional    = true)]
          string? mood = null

        , [NaturalLanguageParam(Description = "Mood score from 1 (lowest) to 5 (highest)."
                              , Optional    = true)]
          int? moodScore = null)
    {
        var isAmendment = _dailyRecordService.GetToday() is not null;
        var taskTitles  = SplitCommaSeparated(tasks);

        var record = await _dailyRecordService.OpenDayAsync( openingText
                                                           , taskTitles
                                                           , mood
                                                           , moodScore);

        if (isAmendment)
        {
            if (taskTitles.Count == 0)
                return "Day updated. No new tasks added.";

            var addedList = BuildTaskList(record.PlannedTaskIds.TakeLast(taskTitles.Count));
            return $"Day updated. {taskTitles.Count} task(s) added to today's plan:\n{addedList}";
        }

        var rolledOver = _dailyRecordService.GetRolledOverTasks();

        var rolledOverNote = rolledOver.Count > 0
                                 ? $"\n\nYou have {rolledOver.Count} task(s) rolled over from a previous day."
                                 + " Say 'claim rolled-over tasks' to add them to today's plan."
                                 : string.Empty;

        if (taskTitles.Count == 0)
            return $"Day opened. No tasks planned yet.{rolledOverNote}";

        var taskList = BuildTaskList(record.PlannedTaskIds);
        return $"Day opened. {taskTitles.Count} task(s) planned:\n{taskList}{rolledOverNote}";
    }

    // =========================================================================
    // AddCheckpoint — intraday check-in
    // =========================================================================

    [FastPath]
    [NaturalLanguageAction(
          Description         = "Add a check-in for today. Call when the user reports progress, completes tasks, or adds new tasks during the day."
        , Examples            =
          [
                  "Check: Finished the streaming handler. Got pulled into a meeting."
                , "Check: Good progress this morning. Tasks: - Write deployment notes"
                , "Checking in — task 3 is done."
          ]
        , Category            = "daily"
        , AllowsClarification = true)]
    public async Task<string> AddCheckpoint(
          [NaturalLanguageParam(Description = "The check-in text — what the user reported."
                              , Optional    = false
                              , AllowEmpty  = false)]
          string text

        , [NaturalLanguageParam(Description = "Pipe-separated IDs of completed tasks, e.g. 'abc123|def456'."
                              , Optional    = true
                              , AllowEmpty  = true)]
          string? completedTaskIds = null

        , [NaturalLanguageParam(Description = "Comma-separated titles of new tasks to add."
                              , Optional    = true
                              , AllowEmpty  = true)]
          string? newTasks = null

        , [NaturalLanguageParam(Description = "The user's mood label at check-in time."
                              , Optional    = true)]
          string? mood = null

        , [NaturalLanguageParam(Description = "Mood score from 1 (lowest) to 5 (highest)."
                              , Optional    = true)]
          int? moodScore = null)
    {
        var completedIds  = SplitPipeSeparated(completedTaskIds);
        var newTaskTitles = SplitCommaSeparated(newTasks);

        var checkpoint = await _dailyRecordService.AddCheckpointAsync( text
                                                                      , completedIds
                                                                      , newTaskTitles
                                                                      , mood
                                                                      , moodScore);

        var parts = new List<string> { "Check-in recorded." };

        if (checkpoint.CompletedTaskIds.Count > 0)
            parts.Add($"{checkpoint.CompletedTaskIds.Count} task(s) completed.");

        if (checkpoint.AddedTaskIds.Count > 0)
        {
            var addedList = BuildTaskList(checkpoint.AddedTaskIds);
            parts.Add($"{checkpoint.AddedTaskIds.Count} new task(s) added:\n{addedList}");
        }

        return string.Join(' ', parts);
    }

    // =========================================================================
    // CloseDay — evening report
    // =========================================================================

    [FastPath]
    [NaturalLanguageAction(
          Description         = "Close today. Call when the user submits their evening report, end-of-day reflection, or signs off for the day."
        , Examples            =
          [
                  "EOD: Good day overall. Didn't finish the docs task."
                , "Done: Solid day. Mood: Satisfied MoodScore: 4"
                , "Evening: Wrapping up. Most things got done."
          ]
        , Category            = "daily"
        , AllowsClarification = false)]
    public async Task<string> CloseDay(
          [NaturalLanguageParam(Description = "The closing text — the user's reflection on the day."
                              , Optional    = false
                              , AllowEmpty  = false)]
          string closingText

        , [NaturalLanguageParam(Description = "The user's end-of-day mood label."
                              , Optional    = true)]
          string? mood = null

        , [NaturalLanguageParam(Description = "Mood score from 1 (lowest) to 5 (highest)."
                              , Optional    = true)]
          int? moodScore = null)
    {
        var record = await _dailyRecordService.CloseDayAsync(closingText, mood, moodScore);

        var rolloverCount = record.PlannedTaskIds.Count
                          + record.ReactiveTaskIds.Count
                          - record.CompletedTaskCount;

        var rolloverNote = rolloverCount > 0
                               ? $" {rolloverCount} task(s) rolled over to tomorrow."
                               : string.Empty;

        var plannedSummary = record.PlannedTaskCount > 0
                                 ? $"{record.CompletedTaskCount}/{record.PlannedTaskCount} planned tasks completed"
                                 + $" ({(int)(record.CompletionRate * 100)}%)."
                                 : "No planned tasks.";

        var reactiveSummary = record.ReactiveTaskCount > 0
                                  ? $" {record.ReactiveTaskCount} reactive task(s) added."
                                  : string.Empty;

        return $"Day closed. {plannedSummary}{reactiveSummary}{rolloverNote}";
    }

    // =========================================================================
    // ListTodaysTasks — show today's task list with referenceable positions
    // =========================================================================

    [FastPath]
    [NaturalLanguageAction(
          Description         = "List today's planned and reactive tasks with their position numbers. Position numbers can be used to complete or delete a task (e.g. 'complete task 3')."
        , Examples            =
          [
                  "What are my tasks today?"
                , "List today's tasks"
                , "Show me today's plan"
                , "What's on my plate today?"
          ]
        , Category            = "daily"
        , AllowsClarification = false)]
    public string ListTodaysTasks()
    {
        var record = _dailyRecordService.GetToday();

        if (record is null)
            return "No plan for today yet. Submit a Plan: to start your day.";

        if (record.PlannedTaskIds.Count == 0 && record.ReactiveTaskIds.Count == 0)
            return $"Today's plan ({record.Id}) has no tasks yet.";

        // Build a position map for all currently active (incomplete) tasks
        var positionByTaskId = _taskService.GetOrderedActiveTasks()
                                           .ToDictionary(pair => pair.Task.Id
                                                        , pair => pair.Position);

        var lines = new List<string> { $"Today's plan ({record.Id}):" };

        void AppendTaskLines(IEnumerable<string> taskIds, string category)
        {
            foreach (var taskId in taskIds)
            {
                var task = _taskService.Get(taskId);
                if (task is null || task.IsDeleted) continue;

                var completed = task.CompletedAt is not null;
                var status    = completed ? "✓" : "○";
                var position  = !completed && positionByTaskId.TryGetValue(taskId, out var pos)
                                    ? $"#{pos,-3}"
                                    : "    ";

                var categoryTag = category == "reactive" ? " [reactive]" : string.Empty;
                lines.Add($"  {status} {position}  {task.ShortDescription}{categoryTag}");
            }
        }

        if (record.PlannedTaskIds.Count > 0)
        {
            lines.Add("");
            lines.Add("  Planned:");
            AppendTaskLines(record.PlannedTaskIds, "planned");
        }

        if (record.ReactiveTaskIds.Count > 0)
        {
            lines.Add("");
            lines.Add("  Reactive:");
            AppendTaskLines(record.ReactiveTaskIds, "reactive");
        }

        var hasIncomplete = record.PlannedTaskIds.Concat(record.ReactiveTaskIds)
                                  .Any(taskId =>
                                  {
                                      var task = _taskService.Get(taskId);
                                      return task is not null && task.CompletedAt is null;
                                  });

        if (hasIncomplete)
            lines.Add("\n  Use 'complete task #N' to mark a task done.");

        return string.Join("\n", lines);
    }

    // =========================================================================
    // ClaimRolledOverTasks — pull previous uncompleted tasks into today's plan
    // =========================================================================

    [FastPath]
    [NaturalLanguageAction(
          Description         = "Claim rolled-over tasks from previous days into today's plan."
        , Examples            =
          [
                  "Claim rolled-over tasks"
                , "Add my rolled-over tasks to today"
                , "Bring forward unfinished tasks"
          ]
        , Category            = "daily"
        , AllowsClarification = false)]
    public async Task<string> ClaimRolledOverTasks()
    {
        var before = _dailyRecordService.GetRolledOverTasks().Count;
        var record = await _dailyRecordService.ClaimRolledOverTasksAsync();

        if (before == 0)
            return "No rolled-over tasks found.";

        var taskList = BuildTaskList(record.PlannedTaskIds);
        return $"Claimed {before} rolled-over task(s) into today's plan:\n{taskList}";
    }

    // =========================================================================
    // DeleteDay — admin reset for erroneous records
    // =========================================================================

    [DestructiveAction]
    [NaturalLanguageAction(
          Description         = "Soft-delete today's daily record so the day can be re-opened fresh. Use only to recover from erroneous test data, an accidental day close, or a timezone glitch."
        , Examples            =
          [
                  "Delete today's daily record"
                , "Reset today's day"
                , "Remove today's plan so I can start over"
          ]
        , Category            = "daily"
        , AllowsClarification = false)]
    public async Task<string> DeleteDay()
    {
        var record = await _dailyRecordService.DeleteTodayAsync();

        return $"Daily record for {record.Id} has been deleted."
             + " You can now open a fresh day with a new Plan:.";
    }

    // =========================================================================
    // Private helpers
    // =========================================================================

    /// <summary>
    /// Builds a numbered task list for display. Active (incomplete) tasks show their
    /// global position number so the user can reference them by position in follow-up
    /// commands (e.g. "complete task 4"). Completed tasks are listed without a position.
    /// </summary>
    private string BuildTaskList(IEnumerable<string> taskIds)
    {
        var positionByTaskId = _taskService.GetOrderedActiveTasks()
                                           .ToDictionary(pair => pair.Task.Id
                                                        , pair => pair.Position);

        var lines = taskIds
            .Select(taskId => _taskService.Get(taskId))
            .Where(task => task is not null && !task.IsDeleted)
            .Select(task =>
            {
                var completed = task!.CompletedAt is not null;
                var status    = completed ? "✓" : "○";
                var position  = !completed && positionByTaskId.TryGetValue(task.Id, out var pos)
                                    ? $"#{pos}"
                                    : string.Empty;
                var posLabel  = position.Length > 0 ? $"{position,-4}" : "    ";
                return $"  {status} {posLabel}  {task.ShortDescription}";
            });

        return string.Join("\n", lines);
    }

    private static IReadOnlyList<string> SplitCommaSeparated(string? input)
        => string.IsNullOrWhiteSpace(input)
               ? Array.Empty<string>()
               : input.Split(',', StringSplitOptions.RemoveEmptyEntries)
                      .Select(value => value.Trim())
                      .Where(value => value.Length > 0)
                      .ToList();

    private static IReadOnlyList<string> SplitPipeSeparated(string? input)
        => string.IsNullOrWhiteSpace(input)
               ? Array.Empty<string>()
               : input.Split('|', StringSplitOptions.RemoveEmptyEntries)
                      .Select(value => value.Trim())
                      .Where(value => value.Length > 0)
                      .ToList();
}
