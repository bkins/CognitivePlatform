using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CognitivePlatform.Api.Domains.Activity;
using CognitivePlatform.Api.Domains.Journal.Interfaces;
using CognitivePlatform.Api.Domains.Meals;
using CognitivePlatform.Api.Domains.Tasks;
using CP.Shared.Primitives.Avails.Extensions;

namespace CognitivePlatform.Api.Domains.Insights;

/// <summary>
/// Aggregates and formats cross-domain telemetry and logs across tasks, journal entries,
/// activity events, and meal records into a structured context prompt for LLM pattern analysis.
/// </summary>
public sealed class PatternDataAggregator : IPatternDataAggregator
{
    private readonly ITaskService    _taskService;
    private readonly IJournalService _journalService;
    private readonly IActivityLog?   _activityLog;
    private readonly IMealService?   _mealService;

    public PatternDataAggregator( ITaskService    taskService
                                , IJournalService journalService
                                , IActivityLog?   activityLog = null
                                , IMealService?   mealService = null )
    {
        _taskService    = taskService    ?? throw new ArgumentNullException(nameof(taskService));
        _journalService = journalService ?? throw new ArgumentNullException(nameof(journalService));
        _activityLog    = activityLog;
        _mealService    = mealService;
    }

    public async Task<string?> AggregateAndFormatAsync(
        string?           focus             = null
      , string?           fromDate          = null
      , string?           toDate            = null
      , CancellationToken cancellationToken = default )
    {
        var from = ParseDate(fromDate);
        var to   = ParseDate(toDate);

        var tasks      = _taskService.List(from, to, includeCompleted: true);
        var entries    = _journalService.ListEntries(from, to);
        var activities = _activityLog is not null
                         ? await _activityLog.ListAsync(from, to, cancellationToken).ConfigureAwait(false)
                         : (IReadOnlyList<ActivityEvent>)Array.Empty<ActivityEvent>();
        var meals      = _mealService is not null
                         ? await _mealService.ListAsync(from, to).ConfigureAwait(false)
                         : (IReadOnlyList<Meal>)Array.Empty<Meal>();

        if (tasks.Count == 0 && entries.Count == 0 && activities.Count == 0 && meals.Count == 0)
            return null;

        var focusLabel = focus.HasValue() ? focus! : "general patterns and trends";

        var sb = new StringBuilder();
        sb.AppendLine("You are a personal productivity and wellbeing assistant. Analyze the tasks, journal entries, logged activities, and dietary meals below and identify patterns, trends, and actionable insights.");
        sb.AppendLine($"Focus area: {focusLabel}");
        sb.AppendLine();

        if (tasks.Count > 0)
        {
            sb.AppendLine("=== Tasks ===");
            foreach (var task in tasks)
            {
                var status = task.CompletedAt.HasValue ? "[Done]"
                           : task.IsDeleted            ? "[Deleted]"
                                                       : "[Active]";
                sb.Append($"{status} {task.ShortDescription}");
                if (task.IsImportant)      sb.Append(" [Important]");
                if (task.IsUrgent)         sb.Append(" [Urgent]");
                if (task.DueDate.HasValue) sb.Append($" (due {task.DueDate:yyyy-MM-dd})");
                if (task.Tags.Count > 0)   sb.Append($" [tags: {string.Join(", ", task.Tags)}]");

                sb.AppendLine();
            }
            sb.AppendLine();
        }

        if (entries.Count > 0)
        {
            sb.AppendLine("=== Journal Entries ===");
            // Cap journal context at 30 entries to keep prompt size reasonable
            foreach (var ewr in entries.Take(30))
            {
                sb.Append($"[{ewr.Entry.CreatedUtc:yyyy-MM-dd}] {ewr.LatestRevision.Text}");
                if (ewr.LatestRevision.Mood is not null)       sb.Append($" [mood: {ewr.LatestRevision.Mood}]");
                if (ewr.LatestRevision.MoodScore.HasValue)     sb.Append($" [mood score: {ewr.LatestRevision.MoodScore}]");
                if (ewr.LatestRevision.Tags is { Count: > 0 }) sb.Append($" [tags: {string.Join(", ", ewr.LatestRevision.Tags)}]");

                sb.AppendLine();
            }
            sb.AppendLine();
        }

        if (activities.Count > 0)
        {
            sb.AppendLine("=== Recent Activities ===");
            // Cap at 50 events to keep prompt size reasonable
            foreach (var activityEvent in activities.Take(50))
            {
                sb.Append($"[{activityEvent.OccurredUtc:yyyy-MM-dd}] {activityEvent.ActivityType}");
                if (activityEvent.Duration.HasValue)
                {
                    sb.Append($" ({activityEvent.Duration.Value}");
                    if (activityEvent.Unit.HasValue()) sb.Append($" {activityEvent.Unit}");

                    sb.Append(')');
                }
                if (activityEvent.Notes.HasValue()) sb.Append($" — {activityEvent.Notes}");
                if (activityEvent.Tags.Count > 0)   sb.Append($" [tags: {string.Join(", ", activityEvent.Tags)}]");

                sb.AppendLine();
            }
            sb.AppendLine();
        }

        if (meals.Count > 0)
        {
            sb.AppendLine("=== Meals ===");
            // Cap at 30 meals to keep prompt size reasonable
            foreach (var meal in meals.Take(30))
            {
                var foods = meal.Foods.Count > 0
                            ? string.Join(", ", meal.Foods.Select(food => food.Name))
                            : "No items";
                sb.Append($"[{meal.ConsumedAt:yyyy-MM-dd}] {meal.MealType}: {foods}");
                if (meal.Notes.HasValue()) sb.Append($" — {meal.Notes}");

                sb.AppendLine();
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static DateTimeOffset? ParseDate(string? input)
    {
        if (input?.HasNoValue() ?? true) return null;

        return DateTimeOffset.TryParse(input, out var result) ? result : null;
    }
}
