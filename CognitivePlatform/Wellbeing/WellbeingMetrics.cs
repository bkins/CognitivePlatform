namespace CognitivePlatform.Api.Wellbeing;

/// <summary>Canonical metric name constants used when creating <see cref="WellbeingSignal"/> records.</summary>
public static class WellbeingMetrics
{
    // Health
    public const string Steps            = "steps";
    public const string SleepMinutes     = "sleep_minutes";
    public const string HeartRateAvg     = "heart_rate_avg";
    public const string DistanceMetres   = "distance_metres";

    // Tasks
    public const string TasksCompleted       = "tasks_completed";
    public const string TasksDue             = "tasks_due";
    public const string TaskCompletionRate   = "task_completion_rate";

    // Journal
    public const string JournalEntryCount = "journal_entry_count";
    public const string JournalWordCount  = "journal_word_count";

    // DailyRecord
    public const string DayOpened            = "day_opened";
    public const string DayClosed            = "day_closed";
    public const string OpenDurationMinutes  = "open_duration_minutes";
}
