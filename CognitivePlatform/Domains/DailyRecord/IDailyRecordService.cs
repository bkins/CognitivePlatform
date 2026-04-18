using CognitivePlatform.Api.Domains.Tasks;

namespace CognitivePlatform.Api.Domains.DailyRecord;

public interface IDailyRecordService
{
    /// <summary>
    /// Opens today's daily record with an intention and optional planned tasks.
    /// Idempotent on the record itself — if today is already open, new tasks are
    /// appended to PlannedTaskIds rather than creating a duplicate record.
    /// Throws if today is already Closed.
    /// </summary>
    Task<DailyRecord> OpenDayAsync( string                  openingText
                                  , IReadOnlyList<string>   taskTitles
                                  , string?                 mood      = null
                                  , int?                    moodScore = null
                                  , IReadOnlyList<string>?  tags      = null);

    /// <summary>
    /// Appends an intraday checkpoint to today's record.
    /// Creates a journal entry tagged #checkin #daily, completes any stated tasks,
    /// and creates new reactive tasks. Advances Phase from Opening → Active.
    /// Throws if today has not been opened or is already Closed.
    /// </summary>
    Task<DailyCheckpoint> AddCheckpointAsync( string                  text
                                            , IReadOnlyList<string>?  completedTaskIds = null
                                            , IReadOnlyList<string>?  newTaskTitles    = null
                                            , string?                 mood             = null
                                            , int?                    moodScore        = null);

    /// <summary>
    /// Closes today's record. Writes the EOD journal entry, computes completion metrics,
    /// and tags uncompleted tasks with "rolled-over:{date}" for next-morning pickup.
    /// Throws if today has not been opened or is already Closed.
    /// </summary>
    Task<DailyRecord> CloseDayAsync( string  closingText
                                   , string? mood      = null
                                   , int?    moodScore = null);

    /// <summary>
    /// Adds all active rolled-over tasks into today's PlannedTaskIds.
    /// Throws if today has not been opened or is already Closed.
    /// </summary>
    Task<DailyRecord> ClaimRolledOverTasksAsync();

    DailyRecord?              GetToday   ();
    DailyRecord?              GetByDate  (DateOnly date);
    IReadOnlyList<DailyRecord> GetRange  (DateOnly from, DateOnly to);

    /// <summary>
    /// Soft-deletes today's daily record so the day can be re-opened fresh.
    /// Use to recover from erroneous test data or an accidental day close.
    /// Throws if no record exists for today.
    /// </summary>
    Task<DailyRecord> DeleteTodayAsync();

    /// <summary>
    /// Returns all active (incomplete) tasks that carry a "rolled-over:" tag,
    /// regardless of which day they originated from.
    /// </summary>
    IReadOnlyList<TaskItem> GetRolledOverTasks();
}
