using Microsoft.Extensions.Logging;

namespace CognitivePlatform.Api.Wellbeing;

/// <summary>
/// Deterministic rule-based pattern detection over a window of <see cref="WellbeingSignal"/> data.
/// No ML — all thresholds are named constants so they can be tuned without a rebuild.
/// </summary>
public sealed class WellbeingPatternService : IWellbeingPatternService
{
    // W.1 thresholds
    private const double SleepDeficitHoursThreshold        = 6.0;
    private const double LowTaskCompletionThreshold         = 0.70;
    private const double RestDayStepThreshold               = 2000.0;
    private const double StepTrendPositiveRatio             = 1.10;   // 10% increase
    private const double StepTrendAttentionRatio            = 0.80;   // 20% decrease
    private const int    MinimumDataDays                    = 3;
    private const int    LowJournalEntryThreshold           = 3;
    private const int    MinOpenCloseDaysForJournalPattern  = 3;

    // W.2 thresholds
    private const double ActivityTrendDropThreshold         = 0.85;   // both steps AND distance down >15%
    private const double SleepStdDevMinutesThreshold        = 90.0;   // inconsistent if population stddev > 90 min
    private const double JournalWordCountDropRatio          = 0.50;   // current avg < 50% of previous avg
    private const int    MinJournalEntriesForWordCountProxy = 3;      // >3 entries required to use proxy
    private const int    MinRecoveryRestDays                = 2;      // ≥2 rest days for a positive recovery signal
    private const int    OpenCloseStreakDays                = 5;      // ≥5 consecutive open+close days

    private readonly IWellbeingSignalStore             _store;
    private readonly ILogger<WellbeingPatternService>  _logger;

    public WellbeingPatternService( IWellbeingSignalStore             store
                                  , ILogger<WellbeingPatternService>  logger )
    {
        _store  = store  ?? throw new ArgumentNullException(nameof(store));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<WellbeingReport> AnalyseAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var fromOffset = ToStartOfDay(from);
        var toOffset   = ToStartOfDay(to);

        var signals = await _store.GetSignalsAsync(fromOffset, toOffset, ct);
        var byDate  = GroupByDate(signals);

        if (byDate.Count < MinimumDataDays)
        {
            return new WellbeingReport
                   {
                       From             = from
                     , To               = to
                     , Patterns         = []
                     , NarrativeSummary = "Not enough data to detect patterns yet — keep tracking for at least 3 days."
                   };
        }

        var windowDays  = to.DayNumber - from.DayNumber + 1;
        var prevTo      = from.AddDays(-1);
        var prevFrom    = from.AddDays(-windowDays);
        var prevSignals = await _store.GetSignalsAsync(ToStartOfDay(prevFrom), ToStartOfDay(prevTo), ct);

        var patterns = new List<WellbeingPattern>();

        // Rule 1: Sleep–task correlation (Concern)
        var sleepTask = DetectSleepTaskCorrelation(byDate);
        if (sleepTask is not null) patterns.Add(sleepTask);

        // Rule 2: Step count trend — previous period for comparison
        var stepTrend = DetectStepCountTrend(signals, prevSignals);
        if (stepTrend is not null) patterns.Add(stepTrend);

        // Rule 3: Journal engagement drop
        var journalEngagement = DetectJournalEngagement(signals, byDate);
        if (journalEngagement is not null) patterns.Add(journalEngagement);

        // Rule 4: Rest day detection (may produce multiple rest days collapsed into one pattern)
        var restDay = DetectRestDays(byDate);
        if (restDay is not null) patterns.Add(restDay);

        // W.2 Rule 5: Weekly activity trend — both steps and distance down >15%
        var activityTrend = DetectWeeklyActivityTrend(signals, prevSignals);
        if (activityTrend is not null) patterns.Add(activityTrend);

        // W.2 Rule 6: Sleep schedule consistency over the window
        var sleepConsistency = DetectSleepConsistency(byDate);
        if (sleepConsistency is not null) patterns.Add(sleepConsistency);

        // W.2 Rule 7: Journal word count drop relative to previous period
        var wordCountDrop = DetectJournalWordCountDrop(signals, prevSignals);
        if (wordCountDrop is not null) patterns.Add(wordCountDrop);

        // W.2 Rule 8: Good recovery — ≥2 rest days AND no Concern patterns already detected
        var goodRecovery = DetectGoodRecovery(byDate, patterns);
        if (goodRecovery is not null) patterns.Add(goodRecovery);

        // W.2 Rule 9: Open/close streak — ≥5 consecutive days with both opened and closed
        var streak = DetectOpenCloseStreak(byDate);
        if (streak is not null) patterns.Add(streak);

        return new WellbeingReport
               {
                   From             = from
                 , To               = to
                 , Patterns         = patterns.AsReadOnly()
                 , NarrativeSummary = BuildNarrative(patterns)
               };
    }

    // ------------------------------------------------------------------
    // Rule 1 — Sleep–task correlation
    // ------------------------------------------------------------------

    private static WellbeingPattern? DetectSleepTaskCorrelation(Dictionary<DateOnly, List<WellbeingSignal>> byDate)
    {
        var sleepHoursPerDay = new List<(DateOnly Date, double Hours)>();
        foreach (var day in byDate)
        {
            var minutes = GetMetricValue(day.Value, WellbeingMetrics.SleepMinutes);
            if (minutes.HasValue)
                sleepHoursPerDay.Add((day.Key, minutes.Value / 60.0));
        }

        var taskRatesPerDay = new List<(DateOnly Date, double Rate)>();
        foreach (var day in byDate)
        {
            var rate = GetMetricValue(day.Value, WellbeingMetrics.TaskCompletionRate);
            if (rate.HasValue)
                taskRatesPerDay.Add((day.Key, rate.Value));
        }

        if (sleepHoursPerDay.Count == 0 || taskRatesPerDay.Count == 0) return null;

        var avgSleepHours = sleepHoursPerDay.Average(entry => entry.Hours);
        var avgTaskRate   = taskRatesPerDay.Average(entry => entry.Rate);

        if (avgSleepHours >= SleepDeficitHoursThreshold || avgTaskRate >= LowTaskCompletionThreshold)
            return null;

        var sharedDates = sleepHoursPerDay
            .Select(entry => entry.Date)
            .Intersect(taskRatesPerDay.Select(entry => entry.Date))
            .ToHashSet();

        var pairs = sharedDates
            .Select(date => (
                Sleep: sleepHoursPerDay.First(entry => entry.Date == date).Hours
              , Rate:  taskRatesPerDay.First(entry => entry.Date == date).Rate
            ))
            .ToList();

        var correlation = pairs.Count >= 2
            ? ComputePearsonCorrelation(pairs.Select(pair => (pair.Sleep, pair.Rate)))
            : (double?)null;

        return new WellbeingPattern
               {
                   Name        = "SleepTaskCorrelation"
                 , Description = $"Your average sleep is {avgSleepHours:F1}h this period and task completion is at {avgTaskRate:P0} — these may be connected."
                 , Sources     = [WellbeingSignalSource.Health, WellbeingSignalSource.Tasks]
                 , Severity    = PatternSeverity.Concern
                 , Correlation = correlation
               };
    }

    // ------------------------------------------------------------------
    // Rule 2 — Step count trend
    // ------------------------------------------------------------------

    private static WellbeingPattern? DetectStepCountTrend(
        IReadOnlyList<WellbeingSignal> currentSignals
      , IReadOnlyList<WellbeingSignal> previousSignals)
    {
        var currentAvg  = GetAverageMetric(currentSignals,  WellbeingMetrics.Steps);
        var previousAvg = GetAverageMetric(previousSignals, WellbeingMetrics.Steps);

        if (!currentAvg.HasValue || !previousAvg.HasValue || previousAvg.Value <= 0) return null;

        var ratio = currentAvg.Value / previousAvg.Value;

        if (ratio >= StepTrendPositiveRatio)
        {
            return new WellbeingPattern
                   {
                       Name        = "StepCountTrend"
                     , Description = $"Your step count is up {ratio - 1.0:P0} compared to the previous period — great movement streak."
                     , Sources     = [WellbeingSignalSource.Health]
                     , Severity    = PatternSeverity.Positive
                     , Correlation = null
                   };
        }

        if (ratio <= StepTrendAttentionRatio)
        {
            return new WellbeingPattern
                   {
                       Name        = "StepCountTrend"
                     , Description = $"Your step count is down {1.0 - ratio:P0} compared to the previous period."
                     , Sources     = [WellbeingSignalSource.Health]
                     , Severity    = PatternSeverity.Attention
                     , Correlation = null
                   };
        }

        return null;
    }

    // ------------------------------------------------------------------
    // Rule 3 — Journal engagement drop
    // ------------------------------------------------------------------

    private static WellbeingPattern? DetectJournalEngagement(
        IReadOnlyList<WellbeingSignal>              signals
      , Dictionary<DateOnly, List<WellbeingSignal>> byDate)
    {
        var totalEntries = (int)signals
            .Where(signal => signal.MetricName == WellbeingMetrics.JournalEntryCount)
            .Sum(signal => signal.Value);

        if (totalEntries >= LowJournalEntryThreshold) return null;

        var openCloseDays = byDate.Count(day =>
        {
            var opened = GetMetricValue(day.Value, WellbeingMetrics.DayOpened);
            var closed = GetMetricValue(day.Value, WellbeingMetrics.DayClosed);
            return opened is >= 1.0 && closed is >= 1.0;
        });

        if (openCloseDays < MinOpenCloseDaysForJournalPattern) return null;

        return new WellbeingPattern
               {
                   Name        = "JournalEngagementDrop"
                 , Description = "You've been opening and closing your days consistently but haven't journalled much this period."
                 , Sources     = [WellbeingSignalSource.Journal, WellbeingSignalSource.DailyRecord]
                 , Severity    = PatternSeverity.Attention
                 , Correlation = null
               };
    }

    // ------------------------------------------------------------------
    // Rule 4 — Rest day detection
    // ------------------------------------------------------------------

    private static WellbeingPattern? DetectRestDays(Dictionary<DateOnly, List<WellbeingSignal>> byDate)
    {
        var restDays = byDate
            .Where(day =>
            {
                var steps          = GetMetricValue(day.Value, WellbeingMetrics.Steps);
                var tasksCompleted = GetMetricValue(day.Value, WellbeingMetrics.TasksCompleted);
                return steps.HasValue
                    && steps.Value < RestDayStepThreshold
                    && (!tasksCompleted.HasValue || tasksCompleted.Value == 0.0);
            })
            .Select(day => day.Key)
            .OrderBy(date => date)
            .ToList();

        if (restDays.Count == 0) return null;

        var description = restDays.Count == 1
            ? $"Rest day detected on {restDays[0]:yyyy-MM-dd} — low steps and no tasks completed."
            : $"{restDays.Count} rest days detected in this period (low steps, no tasks completed).";

        return new WellbeingPattern
               {
                   Name        = "RestDayDetected"
                 , Description = description
                 , Sources     = [WellbeingSignalSource.Health, WellbeingSignalSource.Tasks]
                 , Severity    = PatternSeverity.Neutral
                 , Correlation = null
               };
    }

    // ------------------------------------------------------------------
    // W.2 Rule 5 — Weekly activity trend (steps + distance both down >15%)
    // ------------------------------------------------------------------

    private static WellbeingPattern? DetectWeeklyActivityTrend(
        IReadOnlyList<WellbeingSignal> currentSignals
      , IReadOnlyList<WellbeingSignal> previousSignals)
    {
        var currentStepsAvg    = GetAverageMetric(currentSignals,  WellbeingMetrics.Steps);
        var previousStepsAvg   = GetAverageMetric(previousSignals, WellbeingMetrics.Steps);
        var currentDistanceAvg = GetAverageMetric(currentSignals,  WellbeingMetrics.DistanceMetres);
        var previousDistAvg    = GetAverageMetric(previousSignals, WellbeingMetrics.DistanceMetres);

        if (!currentStepsAvg.HasValue || !previousStepsAvg.HasValue || previousStepsAvg.Value <= 0) return null;
        if (!currentDistanceAvg.HasValue || !previousDistAvg.HasValue || previousDistAvg.Value <= 0) return null;

        var stepsRatio    = currentStepsAvg.Value    / previousStepsAvg.Value;
        var distanceRatio = currentDistanceAvg.Value / previousDistAvg.Value;

        if (stepsRatio > ActivityTrendDropThreshold || distanceRatio > ActivityTrendDropThreshold) return null;

        return new WellbeingPattern
               {
                   Name        = "WeeklyActivityTrend"
                 , Description = "Your activity level has dropped noticeably this week compared to last."
                 , Sources     = [WellbeingSignalSource.Health]
                 , Severity    = PatternSeverity.Attention
                 , Correlation = null
               };
    }

    // ------------------------------------------------------------------
    // W.2 Rule 6 — Sleep schedule consistency (population stddev over window)
    // ------------------------------------------------------------------

    private static WellbeingPattern? DetectSleepConsistency(Dictionary<DateOnly, List<WellbeingSignal>> byDate)
    {
        var sleepMinutesPerDay = byDate
            .Select(day => GetMetricValue(day.Value, WellbeingMetrics.SleepMinutes))
            .Where(minutes => minutes.HasValue)
            .Select(minutes => minutes!.Value)
            .ToList();

        if (sleepMinutesPerDay.Count < MinimumDataDays) return null;

        var mean   = sleepMinutesPerDay.Average();
        var stdDev = Math.Sqrt(sleepMinutesPerDay.Average(minutes => Math.Pow(minutes - mean, 2)));

        if (stdDev <= SleepStdDevMinutesThreshold) return null;

        return new WellbeingPattern
               {
                   Name        = "SleepConsistency"
                 , Description = "Your sleep schedule has been inconsistent this week — irregular sleep patterns can affect energy and focus."
                 , Sources     = [WellbeingSignalSource.Health]
                 , Severity    = PatternSeverity.Attention
                 , Correlation = null
               };
    }

    // ------------------------------------------------------------------
    // W.2 Rule 7 — Journal word count drop relative to previous period
    // ------------------------------------------------------------------

    private static WellbeingPattern? DetectJournalWordCountDrop(
        IReadOnlyList<WellbeingSignal> currentSignals
      , IReadOnlyList<WellbeingSignal> previousSignals)
    {
        var totalEntries = (int)currentSignals
            .Where(signal => signal.MetricName == WellbeingMetrics.JournalEntryCount)
            .Sum(signal => signal.Value);

        if (totalEntries <= MinJournalEntriesForWordCountProxy) return null;

        var currentWordCountAvg  = GetAverageMetric(currentSignals,  WellbeingMetrics.JournalWordCount);
        var previousWordCountAvg = GetAverageMetric(previousSignals, WellbeingMetrics.JournalWordCount);

        if (!currentWordCountAvg.HasValue || !previousWordCountAvg.HasValue || previousWordCountAvg.Value <= 0) return null;

        var ratio = currentWordCountAvg.Value / previousWordCountAvg.Value;
        if (ratio >= JournalWordCountDropRatio) return null;

        return new WellbeingPattern
               {
                   Name        = "JournalWordCountDrop"
                 , Description = "You're journaling less than usual this week."
                 , Sources     = [WellbeingSignalSource.Journal]
                 , Severity    = PatternSeverity.Attention
                 , Correlation = null
               };
    }

    // ------------------------------------------------------------------
    // W.2 Rule 8 — Good recovery (≥2 rest days, no Concern patterns)
    // ------------------------------------------------------------------

    private static WellbeingPattern? DetectGoodRecovery(
        Dictionary<DateOnly, List<WellbeingSignal>> byDate
      , IReadOnlyCollection<WellbeingPattern>       detectedPatterns)
    {
        if (detectedPatterns.Any(pattern => pattern.Severity == PatternSeverity.Concern)) return null;

        var restDayCount = byDate.Count(day =>
        {
            var steps = GetMetricValue(day.Value, WellbeingMetrics.Steps);
            return steps.HasValue && steps.Value < RestDayStepThreshold;
        });

        if (restDayCount < MinRecoveryRestDays) return null;

        return new WellbeingPattern
               {
                   Name        = "GoodRecovery"
                 , Description = "You've taken some good recovery time this week."
                 , Sources     = [WellbeingSignalSource.Health]
                 , Severity    = PatternSeverity.Positive
                 , Correlation = null
               };
    }

    // ------------------------------------------------------------------
    // W.2 Rule 9 — Open/close streak (≥5 consecutive days)
    // ------------------------------------------------------------------

    private static WellbeingPattern? DetectOpenCloseStreak(Dictionary<DateOnly, List<WellbeingSignal>> byDate)
    {
        var openCloseDates = byDate
            .Where(day =>
            {
                var opened = GetMetricValue(day.Value, WellbeingMetrics.DayOpened);
                var closed = GetMetricValue(day.Value, WellbeingMetrics.DayClosed);
                return opened is >= 1.0 && closed is >= 1.0;
            })
            .Select(day => day.Key)
            .OrderBy(date => date)
            .ToList();

        if (openCloseDates.Count < OpenCloseStreakDays) return null;

        var maxStreak     = 1;
        var currentStreak = 1;

        for (var i = 1; i < openCloseDates.Count; i++)
        {
            if (openCloseDates[i] == openCloseDates[i - 1].AddDays(1))
            {
                currentStreak++;
                if (currentStreak > maxStreak) maxStreak = currentStreak;
            }
            else
            {
                currentStreak = 1;
            }
        }

        if (maxStreak < OpenCloseStreakDays) return null;

        return new WellbeingPattern
               {
                   Name        = "OpenCloseStreak"
                 , Description = "You've been consistently opening and closing your days — great habit!"
                 , Sources     = [WellbeingSignalSource.DailyRecord]
                 , Severity    = PatternSeverity.Positive
                 , Correlation = null
               };
    }

    // ------------------------------------------------------------------
    // Narrative synthesis
    // ------------------------------------------------------------------

    private static string BuildNarrative(List<WellbeingPattern> patterns)
    {
        if (patterns.Count == 0)
            return "Things look balanced over this period — no notable patterns detected.";

        var sentences = new List<string>();

        // Surface the most actionable signals first: Concern → Attention → Positive → Neutral
        foreach (var severity in new[] { PatternSeverity.Concern, PatternSeverity.Attention, PatternSeverity.Positive, PatternSeverity.Neutral })
        {
            foreach (var pattern in patterns.Where(p => p.Severity == severity))
            {
                sentences.Add(pattern.Description);
                if (sentences.Count == 3) break;
            }
            if (sentences.Count == 3) break;
        }

        return string.Join(" ", sentences);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static Dictionary<DateOnly, List<WellbeingSignal>> GroupByDate(IReadOnlyList<WellbeingSignal> signals)
    {
        var result = new Dictionary<DateOnly, List<WellbeingSignal>>();

        foreach (var signal in signals)
        {
            if (!result.TryGetValue(signal.Date, out var daySignals))
            {
                daySignals = [];
                result[signal.Date] = daySignals;
            }
            daySignals.Add(signal);
        }

        return result;
    }

    private static double? GetMetricValue(List<WellbeingSignal> signals, string metricName)
    {
        var signal = signals.FirstOrDefault(s => s.MetricName == metricName);
        return signal is not null ? signal.Value : (double?)null;
    }

    private static double? GetAverageMetric(IReadOnlyList<WellbeingSignal> signals, string metricName)
    {
        var values = signals
            .Where(signal => signal.MetricName == metricName)
            .Select(signal => signal.Value)
            .ToList();

        return values.Count > 0 ? values.Average() : (double?)null;
    }

    private static double? ComputePearsonCorrelation(IEnumerable<(double x, double y)> pairs)
    {
        var pairList = pairs.ToList();
        if (pairList.Count < 2) return null;

        var meanX = pairList.Average(pair => pair.x);
        var meanY = pairList.Average(pair => pair.y);

        var numerator   = pairList.Sum(pair => (pair.x - meanX) * (pair.y - meanY));
        var denomX      = Math.Sqrt(pairList.Sum(pair => Math.Pow(pair.x - meanX, 2)));
        var denomY      = Math.Sqrt(pairList.Sum(pair => Math.Pow(pair.y - meanY, 2)));
        var denominator = denomX * denomY;

        if (denominator == 0.0) return null;

        return Math.Clamp(numerator / denominator, -1.0, 1.0);
    }

    private static DateTimeOffset ToStartOfDay(DateOnly date) =>
        new(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
}
