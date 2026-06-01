using System.Runtime.CompilerServices;
using CognitivePlatform.Api.Conversation;
using CognitivePlatform.Api.Domains.Journal.Interfaces;
using CognitivePlatform.Api.Domains.Tasks;
using CognitivePlatform.Api.Insights.Models;
using CognitivePlatform.Api.Integrations.Health;

namespace CognitivePlatform.Api.Insights;

/// <summary>
/// Phase C (ENH-31) insight provider. Detects gradual positive trends the user may not
/// consciously notice — purely rule-based, intentionally positive-only.
///
/// Three signal types (checked in priority order; the strongest fires first):
///   1. Journaling streak — ≥ 5 consecutive days with at least one entry.
///   2. Task completion rate — this-week completions ≥ 20% above last-week completions.
///   3. Sleep consistency — σ of daily sleep duration &lt; 30 minutes over the last 7 days.
///
/// <para>
/// Trigger:          Any one of the three signals above is detected.
/// Message:          One brief positive observation surfaced per turn.
/// DeduplicationKey: habit.{signalType}
/// RepeatWindow:     72 hours per signal (Habit category window enforced by InsightPolicy).
/// InsightCategory:  Habit
/// </para>
/// </summary>
public sealed class HabitReinforcementInsightProvider : IInsightProvider
{
    private const int    JournalingStreakThreshold  = 5;
    private const double CompletionImprovementRate  = 0.2;
    private const double SleepConsistencyThresholdMinutes = 30.0;

    private readonly IJournalService _journalService;
    private readonly ITaskService    _taskService;
    private readonly IHealthProvider _healthProvider;

    public InsightCategory Category => InsightCategory.Habit;

    public HabitReinforcementInsightProvider( IJournalService journalService
                                            , ITaskService    taskService
                                            , IHealthProvider healthProvider )
    {
        _journalService = journalService ?? throw new ArgumentNullException(nameof(journalService));
        _taskService    = taskService    ?? throw new ArgumentNullException(nameof(taskService));
        _healthProvider = healthProvider ?? throw new ArgumentNullException(nameof(healthProvider));
    }

    public async IAsyncEnumerable<Insight> GenerateAsync(
        ConversationContext                        context
      , [EnumeratorCancellation] CancellationToken cancellationToken = default )
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Candidates are checked in priority order; first match wins.
        var best = CheckJournalingStreak()
                ?? CheckTaskCompletionRate()
                ?? await CheckSleepConsistencyAsync(cancellationToken);

        if (best is not null)
            yield return best;
    }

    // ------------------------------------------------------------------
    // Signal 1 — Journaling streak
    // ------------------------------------------------------------------

    private Insight? CheckJournalingStreak()
    {
        var entries = _journalService.ListEntries(fromUtc: DateTimeOffset.UtcNow.AddDays(-10));

        var entryDateSet = entries
            .Select(entry => entry.Entry.CreatedUtc.LocalDateTime.Date)
            .ToHashSet();

        var today     = DateTime.Today;
        var streak    = 0;
        var checkDate = today;

        while (entryDateSet.Contains(checkDate))
        {
            streak++;
            checkDate = checkDate.AddDays(-1);
        }

        if (streak < JournalingStreakThreshold)
            return null;

        return new Insight
               {
                       Message          = $"You've journaled every day for {streak} days — that's your consistency showing."
                     , DeduplicationKey = "habit.journaling-streak"
                     , Category         = InsightCategory.Habit
                     , Priority         = InsightPriority.Normal
                     , Reasoning        = new InsightReasoning
                                         {
                                                 Explanation = $"Journaling streak: {streak} consecutive days."
                                         }
               };
    }

    // ------------------------------------------------------------------
    // Signal 2 — Task completion rate improvement
    // ------------------------------------------------------------------

    private Insight? CheckTaskCompletionRate()
    {
        var now             = DateTimeOffset.UtcNow;
        var sevenDaysAgo    = now.AddDays(-7);
        var fourteenDaysAgo = now.AddDays(-14);

        var completed = _taskService.GetCompleted();

        var thisWeekCount = completed.Count(task => task.CompletedAt.HasValue
                                                 && task.CompletedAt.Value >= sevenDaysAgo);

        var lastWeekCount = completed.Count(task => task.CompletedAt.HasValue
                                                 && task.CompletedAt.Value >= fourteenDaysAgo
                                                 && task.CompletedAt.Value < sevenDaysAgo);

        if (thisWeekCount < 2) return null;

        var improvement = (double)(thisWeekCount - lastWeekCount) / Math.Max(lastWeekCount, 1);

        if (improvement < CompletionImprovementRate) return null;

        return new Insight
               {
                       Message          = $"Your task completion is up this week — {thisWeekCount} done versus {lastWeekCount} last week. Nice momentum."
                     , DeduplicationKey = "habit.task-completion-rate"
                     , Category         = InsightCategory.Habit
                     , Priority         = InsightPriority.Normal
                     , Reasoning        = new InsightReasoning
                                         {
                                                 Explanation = $"Task completions: {thisWeekCount} this week vs {lastWeekCount} last week ({improvement:P0} improvement)."
                                         }
               };
    }

    // ------------------------------------------------------------------
    // Signal 3 — Sleep consistency
    // ------------------------------------------------------------------

    private async Task<Insight?> CheckSleepConsistencyAsync(CancellationToken cancellationToken)
    {
        if (!_healthProvider.IsConnected) return null;

        var today        = DateTimeOffset.UtcNow.Date;
        var sleepMinutes = new List<double>(7);

        for (var offset = 1; offset <= 7; offset++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var dayStart = new DateTimeOffset(today.AddDays(-offset), TimeSpan.Zero);
            var result   = await _healthProvider.GetSleepAsync(dayStart, dayStart.AddDays(1), cancellationToken);

            if (result.TotalMinutes > 0)
                sleepMinutes.Add(result.TotalMinutes);
        }

        if (sleepMinutes.Count < 5) return null;

        var mean  = sleepMinutes.Average();
        var sigma = Math.Sqrt(sleepMinutes.Average(minutes => (minutes - mean) * (minutes - mean)));

        if (sigma >= SleepConsistencyThresholdMinutes) return null;

        return new Insight
               {
                       Message          = $"Your sleep has been remarkably consistent this week — averaging {mean / 60:F1}h with very little variation."
                     , DeduplicationKey = "habit.sleep-consistency"
                     , Category         = InsightCategory.Habit
                     , Priority         = InsightPriority.Normal
                     , Reasoning        = new InsightReasoning
                                         {
                                                 Explanation = $"Sleep duration σ = {sigma:F1} min over 7 days (threshold: {SleepConsistencyThresholdMinutes} min)."
                                         }
               };
    }
}
