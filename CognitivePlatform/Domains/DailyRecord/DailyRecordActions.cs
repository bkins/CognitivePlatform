using CognitivePlatform.Api.Attributes;

namespace CognitivePlatform.Api.Domains.DailyRecord;

public class DailyRecordActions
{
    private readonly IDailyRecordService _dailyRecordService;

    public DailyRecordActions(IDailyRecordService dailyRecordService)
    {
        _dailyRecordService = dailyRecordService ?? throw new ArgumentNullException(nameof(dailyRecordService));
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

        await _dailyRecordService.OpenDayAsync( openingText
                                              , taskTitles
                                              , mood
                                              , moodScore);

        if (isAmendment)
        {
            var addedText = taskTitles.Count > 0
                                ? $"{taskTitles.Count} task(s) added to today's plan."
                                : "No new tasks added.";
            return $"Day updated. {addedText}";
        }

        var openedText = taskTitles.Count > 0
                             ? $"{taskTitles.Count} task(s) planned."
                             : "No tasks planned yet.";

        var rolledOver = _dailyRecordService.GetRolledOverTasks();

        var rolledOverNote = rolledOver.Count > 0
                                 ? $"\n\nYou have {rolledOver.Count} task(s) rolled over from a previous day."
                                 + " Say 'claim rolled-over tasks' to add them to today's plan."
                                 : string.Empty;

        return $"Day opened. {openedText}{rolledOverNote}";
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
            parts.Add($"{checkpoint.AddedTaskIds.Count} new task(s) added.");

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

        var rate = (int)(record.CompletionRate * 100);

        var rolloverCount = record.PlannedTaskIds.Count
                          + record.ReactiveTaskIds.Count
                          - record.CompletedTaskCount;

        var rolloverNote = rolloverCount > 0
                               ? $" {rolloverCount} task(s) rolled over to tomorrow."
                               : string.Empty;

        return $"Day closed. {record.CompletedTaskCount}/{record.PlannedTaskCount} planned tasks completed ({rate}%)."
             + $" {record.ReactiveTaskCount} reactive task(s) added.{rolloverNote}";
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

        return before == 0
                   ? "No rolled-over tasks found."
                   : $"Claimed {before} rolled-over task(s) into today's plan."
                   + $" Today now has {record.PlannedTaskIds.Count} planned task(s).";
    }

    // =========================================================================
    // Private helpers
    // =========================================================================

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
