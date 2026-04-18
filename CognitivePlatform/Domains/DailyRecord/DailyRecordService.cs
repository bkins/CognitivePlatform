using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Domains.Journal.Interfaces;
using CognitivePlatform.Api.Domains.Tasks;
using CP.Shared.Primitives.Avails.Extensions;

namespace CognitivePlatform.Api.Domains.DailyRecord;

/// DOMAIN SERVICE
/// ----------------
/// Owns the daily record lifecycle: open, checkpoint, close.
/// Delegates all task mutations to ITaskService and all journal writes to IJournalService.
/// Never writes directly to task or journal storage.
public class DailyRecordService : IDailyRecordService
{
    private readonly IObjectStore    _store;
    private readonly ITaskService    _taskService;
    private readonly IJournalService _journalService;

    public DailyRecordService( IObjectStore    store
                              , ITaskService    taskService
                              , IJournalService journalService)
    {
        _store          = store;
        _taskService    = taskService;
        _journalService = journalService;
    }

    public async Task<DailyRecord> OpenDayAsync( string                  openingText
                                               , IReadOnlyList<string>   taskTitles
                                               , string?                 mood      = null
                                               , int?                    moodScore = null
                                               , IReadOnlyList<string>?  tags      = null)
    {
        var today  = TodayKey();
        var record = _store.Get<DailyRecord>(today);

        // A soft-deleted record means the day was intentionally reset via DeleteTodayAsync.
        // Treat it as if it doesn't exist so the user can open a fresh record for the same date.
        if (record?.IsDeleted == true)
            record = null;

        if (record is not null && record.Phase == DayPhase.Closed)
            throw new InvalidOperationException($"Day {today} is already closed and cannot be modified.");

        if (record is null)
        {
            var journalTags = BuildJournalTags(new[] { "#plan", "#daily" }, tags);
            var journalId   = await _journalService.AddEntryAsync( openingText
                                                                 , journalTags
                                                                 , mood
                                                                 , moodScore
                                                                 , moodLevel:   null
                                                                 , mediaPaths:  Array.Empty<string>());

            record = new DailyRecord
                     {
                             Id               = today
                           , Date             = DateOnly.Parse(today)
                           , Phase            = DayPhase.Opening
                           , OpeningJournalId = journalId
                           , OpeningText      = openingText
                           , OpenedAtUtc      = DateTimeOffset.UtcNow
                           , Mood             = mood
                           , MoodScore        = moodScore
                     };
        }

        if (taskTitles.Count > 0)
        {
            var originDate = DateOnly.Parse(today);

            foreach (var title in taskTitles)
            {
                var task = _taskService.Create(new TaskItem
                                               {
                                                       ShortDescription = title
                                                     , OriginDate       = originDate
                                               });
                record.PlannedTaskIds.Add(task.Id);
            }
        }

        await _store.Save(record, partitionKey: null, id: record.Id);

        return record;
    }

    public async Task<DailyCheckpoint> AddCheckpointAsync( string                  text
                                                          , IReadOnlyList<string>?  completedTaskIds = null
                                                          , IReadOnlyList<string>?  newTaskTitles    = null
                                                          , string?                 mood             = null
                                                          , int?                    moodScore        = null)
    {
        var record    = RequireOpenRecord();
        var journalId = await _journalService.AddEntryAsync( text
                                                           , new[] { "#checkin", "#daily" }
                                                           , mood
                                                           , moodScore
                                                           , moodLevel:  null
                                                           , mediaPaths: Array.Empty<string>());

        var checkpoint = new DailyCheckpoint
                         {
                                 DailyRecordId  = record.Id
                               , JournalEntryId = journalId
                               , CreatedAtUtc   = DateTimeOffset.UtcNow
                               , Text           = text
                         };

        foreach (var taskId in completedTaskIds ?? Array.Empty<string>())
        {
            _taskService.Complete(taskId);
            checkpoint.CompletedTaskIds.Add(taskId);
        }

        var originDate = DateOnly.Parse(record.Id);

        foreach (var title in newTaskTitles ?? Array.Empty<string>())
        {
            var task = _taskService.Create(new TaskItem
                                           {
                                                   ShortDescription = title
                                                 , OriginDate       = originDate
                                           });
            checkpoint.AddedTaskIds.Add(task.Id);
            record.ReactiveTaskIds.Add(task.Id);
        }

        if (record.Phase == DayPhase.Opening)
            record.Phase = DayPhase.Active;

        record.CheckpointIds.Add(checkpoint.Id);

        await _store.Save(checkpoint, partitionKey: null, id: checkpoint.Id);
        await _store.Save(record,    partitionKey: null, id: record.Id);

        return checkpoint;
    }

    public async Task<DailyRecord> CloseDayAsync( string  closingText
                                                 , string? mood      = null
                                                 , int?    moodScore = null)
    {
        var record    = RequireOpenRecord();
        var journalId = await _journalService.AddEntryAsync( closingText
                                                           , new[] { "#eod", "#daily" }
                                                           , mood
                                                           , moodScore
                                                           , moodLevel:  null
                                                           , mediaPaths: Array.Empty<string>());

        var rolledOverTag = $"rolled-over:{record.Id}";
        var allTaskIds    = record.PlannedTaskIds
                                  .Concat(record.ReactiveTaskIds)
                                  .Distinct();

        foreach (var taskId in allTaskIds)
        {
            var task = _taskService.Get(taskId);

            if (task is null || task.CompletedAt != null)
                continue;

            task.Tags.Add(rolledOverTag);
            _taskService.Update(task);
        }

        var completedCount = record.PlannedTaskIds
                                   .Select(taskId => _taskService.Get(taskId))
                                   .Count(task => task?.CompletedAt != null);

        record.PlannedTaskCount   = record.PlannedTaskIds.Count;
        record.CompletedTaskCount = completedCount;
        record.ReactiveTaskCount  = record.ReactiveTaskIds.Count;
        record.CompletionRate     = record.PlannedTaskCount > 0
                                        ? completedCount / (double)record.PlannedTaskCount
                                        : 0.0;
        record.ClosingJournalId   = journalId;
        record.ClosingText        = closingText;
        record.ClosedAtUtc        = DateTimeOffset.UtcNow;
        record.Phase              = DayPhase.Closed;

        if (mood      is not null) record.Mood      = mood;
        if (moodScore is not null) record.MoodScore = moodScore;

        await _store.Save(record, partitionKey: null, id: record.Id);

        return record;
    }

    public async Task<DailyRecord> ClaimRolledOverTasksAsync()
    {
        var record          = RequireOpenRecord();
        var rolledOverTasks = GetRolledOverTasks();

        foreach (var task in rolledOverTasks)
        {
            if (!record.PlannedTaskIds.Contains(task.Id))
                record.PlannedTaskIds.Add(task.Id);
        }

        await _store.Save(record, partitionKey: null, id: record.Id);

        return record;
    }

    public DailyRecord? GetToday()
    {
        var record = _store.Get<DailyRecord>(TodayKey());
        return record?.IsDeleted == true ? null : record;
    }

    public async Task<DailyRecord> DeleteTodayAsync()
    {
        var record = _store.Get<DailyRecord>(TodayKey())
                     ?? throw new InvalidOperationException(
                            "No daily record exists for today — nothing to delete.");

        record.IsDeleted  = true;
        record.DeletedUtc = DateTimeOffset.UtcNow;

        await _store.Save(record, partitionKey: null, id: record.Id);

        return record;
    }

    public DailyRecord? GetByDate(DateOnly date)
        => _store.Get<DailyRecord>(date.ToString("yyyy-MM-dd"));

    public IReadOnlyList<DailyRecord> GetRange(DateOnly from, DateOnly to)
        => _store.List<DailyRecord>()
                 .Where(record => record.Date >= from
                               && record.Date <= to
                               && record.IsDeleted.Not())
                 .OrderBy(record => record.Date)
                 .ToList();

    public IReadOnlyList<TaskItem> GetRolledOverTasks()
        => _taskService.List(includeCompleted: false)
                       .Where(task => task.Tags.Any(tag =>
                           tag.StartsWith("rolled-over:", StringComparison.OrdinalIgnoreCase)))
                       .ToList();

    // --- Private helpers ---------------------------------------------------------

    private static string TodayKey()
    {
        // Development override: set CP_DAILY_DATE=yyyy-MM-dd to simulate a different day
        // without waiting for the real calendar to advance.
        var envOverride = Environment.GetEnvironmentVariable("CP_DAILY_DATE");

        if (!string.IsNullOrWhiteSpace(envOverride)
         && DateOnly.TryParse(envOverride, out var overrideDate))
            return overrideDate.ToString("yyyy-MM-dd");

        return DateOnly.FromDateTime(DateTime.Now).ToString("yyyy-MM-dd");
    }

    private DailyRecord RequireOpenRecord()
    {
        var record = GetToday();

        if (record is null)
            throw new InvalidOperationException(
                "No daily record exists for today. Submit a Plan: first.");

        if (record.Phase == DayPhase.Closed)
            throw new InvalidOperationException($"Day {record.Id} is already closed.");

        return record;
    }

    private static IReadOnlyList<string> BuildJournalTags( IEnumerable<string>     systemTags
                                                          , IReadOnlyList<string>?  userTags)
        => systemTags
               .Concat(userTags ?? Array.Empty<string>())
               .Distinct(StringComparer.OrdinalIgnoreCase)
               .ToList();
}
