using CognitivePlatform.Api.Domains.DailyRecord;
using CognitivePlatform.Api.Domains.Journal.Interfaces;
using CognitivePlatform.Api.Domains.Tasks;
using CognitivePlatform.Api.Integrations.Health;
using Microsoft.Extensions.Logging;

namespace CognitivePlatform.Api.Wellbeing;

/// <summary>
/// Collects wellbeing signals from Health, Tasks, Journal, and DailyRecord for a
/// given calendar date. Health signals are silently omitted when the phone is unreachable
/// or throws — all other sources continue regardless. Each collected signal is persisted
/// to <see cref="IWellbeingSignalStore"/> using an upsert so re-collection is idempotent.
/// </summary>
public sealed class WellbeingSignalCollector : IWellbeingSignalCollector
{
    private static readonly char[] WordSeparators = [' ', '\t', '\n', '\r'];

    private readonly IHealthProvider                   _healthProvider;
    private readonly ITaskService                      _taskService;
    private readonly IJournalService                   _journalService;
    private readonly IDailyRecordService               _dailyRecordService;
    private readonly IWellbeingSignalStore             _store;
    private readonly ILogger<WellbeingSignalCollector> _logger;

    public WellbeingSignalCollector( IHealthProvider                   healthProvider
                                   , ITaskService                      taskService
                                   , IJournalService                   journalService
                                   , IDailyRecordService               dailyRecordService
                                   , IWellbeingSignalStore             store
                                   , ILogger<WellbeingSignalCollector> logger )
    {
        _healthProvider     = healthProvider     ?? throw new ArgumentNullException(nameof(healthProvider));
        _taskService        = taskService        ?? throw new ArgumentNullException(nameof(taskService));
        _journalService     = journalService     ?? throw new ArgumentNullException(nameof(journalService));
        _dailyRecordService = dailyRecordService ?? throw new ArgumentNullException(nameof(dailyRecordService));
        _store              = store              ?? throw new ArgumentNullException(nameof(store));
        _logger             = logger             ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<WellbeingSignal>> CollectSignalsAsync(
        DateOnly          date
      , CancellationToken cancellationToken = default)
    {
        var dayStart = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var dayEnd   = dayStart.AddDays(1);

        // Health queries are async network calls — start them before processing local sources.
        var healthTask = _healthProvider.IsConnected
            ? CollectHealthSignalsAsync(date, dayStart, dayEnd, cancellationToken)
            : Task.FromResult<IReadOnlyList<WellbeingSignal>>([]);

        // Local sources are synchronous and fast — collect inline.
        var signals = new List<WellbeingSignal>();
        signals.AddRange(CollectTaskSignals(date, dayStart, dayEnd));
        signals.AddRange(CollectJournalSignals(date, dayStart, dayEnd));
        signals.AddRange(CollectDailyRecordSignals(date));

        signals.AddRange(await healthTask);

        foreach (var signal in signals)
            await _store.SaveSignalAsync(signal, cancellationToken);

        return signals.AsReadOnly();
    }

    // ------------------------------------------------------------------
    // Health
    // ------------------------------------------------------------------

    private async Task<IReadOnlyList<WellbeingSignal>> CollectHealthSignalsAsync(
        DateOnly          date
      , DateTimeOffset    dayStart
      , DateTimeOffset    dayEnd
      , CancellationToken cancellationToken)
    {
        try
        {
            var stepsTask     = _healthProvider.GetStepCountAsync(dayStart, dayEnd, cancellationToken);
            var sleepTask     = _healthProvider.GetSleepAsync(dayStart, dayEnd, cancellationToken);
            var heartRateTask = _healthProvider.GetHeartRateAsync(dayStart, dayEnd, cancellationToken);
            var distanceTask  = _healthProvider.GetDistanceAsync(dayStart, dayEnd, cancellationToken);

            await Task.WhenAll(stepsTask, sleepTask, heartRateTask, distanceTask);

            return
            [
                Signal(date, WellbeingSignalSource.Health, WellbeingMetrics.Steps          , stepsTask.Result.Steps           , "count"  )
              , Signal(date, WellbeingSignalSource.Health, WellbeingMetrics.SleepMinutes   , sleepTask.Result.TotalMinutes     , "minutes")
              , Signal(date, WellbeingSignalSource.Health, WellbeingMetrics.HeartRateAvg   , heartRateTask.Result.AverageBpm   , "bpm"    )
              , Signal(date, WellbeingSignalSource.Health, WellbeingMetrics.DistanceMetres , distanceTask.Result.Metres        , "metres" )
            ];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Health signal collection failed for {Date} — health signals will be absent", date);
            return [];
        }
    }

    // ------------------------------------------------------------------
    // Tasks
    // ------------------------------------------------------------------

    private IReadOnlyList<WellbeingSignal> CollectTaskSignals(
        DateOnly       date
      , DateTimeOffset dayStart
      , DateTimeOffset dayEnd)
    {
        try
        {
            var allTasks = _taskService.List(includeCompleted: true);

            var completedOnDate = allTasks
                .Where(task => task.CompletedAt.HasValue
                            && task.CompletedAt.Value >= dayStart
                            && task.CompletedAt.Value < dayEnd)
                .ToList();

            var dueOnDate = allTasks
                .Where(task => task.DueDate.HasValue
                            && task.DueDate.Value >= dayStart
                            && task.DueDate.Value < dayEnd)
                .ToList();

            var signals = new List<WellbeingSignal>
            {
                Signal(date, WellbeingSignalSource.Tasks, WellbeingMetrics.TasksCompleted , completedOnDate.Count, "count")
              , Signal(date, WellbeingSignalSource.Tasks, WellbeingMetrics.TasksDue       , dueOnDate.Count      , "count")
            };

            if (dueOnDate.Count > 0)
            {
                var completedDueCount = dueOnDate
                    .Count(task => task.CompletedAt.HasValue
                                && task.CompletedAt.Value >= dayStart
                                && task.CompletedAt.Value < dayEnd);

                signals.Add(Signal(date
                                 , WellbeingSignalSource.Tasks
                                 , WellbeingMetrics.TaskCompletionRate
                                 , (double)completedDueCount / dueOnDate.Count
                                 , "ratio"));
            }

            return signals;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Task signal collection failed for {Date}", date);
            return [];
        }
    }

    // ------------------------------------------------------------------
    // Journal
    // ------------------------------------------------------------------

    private IReadOnlyList<WellbeingSignal> CollectJournalSignals(
        DateOnly       date
      , DateTimeOffset dayStart
      , DateTimeOffset dayEnd)
    {
        try
        {
            var entries = _journalService.ListEntries(fromUtc: dayStart, toUtc: dayEnd);

            var wordCount = entries.Sum(entry =>
                entry.LatestRevision.Text
                     .Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries)
                     .Length);

            return
            [
                Signal(date, WellbeingSignalSource.Journal, WellbeingMetrics.JournalEntryCount , entries.Count , "count")
              , Signal(date, WellbeingSignalSource.Journal, WellbeingMetrics.JournalWordCount   , wordCount     , "count")
            ];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Journal signal collection failed for {Date}", date);
            return [];
        }
    }

    // ------------------------------------------------------------------
    // DailyRecord
    // ------------------------------------------------------------------

    private IReadOnlyList<WellbeingSignal> CollectDailyRecordSignals(DateOnly date)
    {
        try
        {
            var record = _dailyRecordService.GetByDate(date);

            var dayOpened = record?.OpenedAtUtc.HasValue == true ? 1.0 : 0.0;
            var dayClosed = record?.ClosedAtUtc.HasValue == true ? 1.0 : 0.0;

            var signals = new List<WellbeingSignal>
            {
                Signal(date, WellbeingSignalSource.DailyRecord, WellbeingMetrics.DayOpened , dayOpened, null)
              , Signal(date, WellbeingSignalSource.DailyRecord, WellbeingMetrics.DayClosed , dayClosed, null)
            };

            if (record?.OpenedAtUtc.HasValue == true && record.ClosedAtUtc.HasValue)
            {
                var durationMinutes = (record.ClosedAtUtc.Value - record.OpenedAtUtc.Value).TotalMinutes;
                signals.Add(Signal(date
                                 , WellbeingSignalSource.DailyRecord
                                 , WellbeingMetrics.OpenDurationMinutes
                                 , durationMinutes
                                 , "minutes"));
            }

            return signals;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "DailyRecord signal collection failed for {Date}", date);
            return [];
        }
    }

    // ------------------------------------------------------------------
    // Factory helper
    // ------------------------------------------------------------------

    private static WellbeingSignal Signal(
        DateOnly             date
      , WellbeingSignalSource source
      , string               metricName
      , double               value
      , string?              unit) =>
        new()
        {
                Id          = $"{source}.{metricName}.{date:yyyy-MM-dd}"
              , Date        = date
              , Source      = source
              , MetricName  = metricName
              , Value       = value
              , Unit        = unit
              , CollectedAt = DateTimeOffset.UtcNow
        };
}
