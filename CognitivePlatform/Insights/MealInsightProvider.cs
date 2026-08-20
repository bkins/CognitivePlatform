using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using CognitivePlatform.Api.Conversation;
using CognitivePlatform.Api.Domains.Journal.Interfaces;
using CognitivePlatform.Api.Domains.Meals;
using CognitivePlatform.Api.Domains.Tasks;
using CognitivePlatform.Api.Insights.Models;
using CognitivePlatform.Api.Integrations.Health;
using CP.Shared.Primitives.Avails.Extensions;

namespace CognitivePlatform.Api.Insights;

/// <summary>
/// Proactively correlates dietary habits (e.g., late dinners, late caffeine, sugar triggers, and protein intake)
/// with next-day sleep telemetry, journaled mood scores, and task completion volume over a rolling 14-day window.
/// </summary>
public sealed class MealInsightProvider : IInsightProvider
{
    private const int    WindowDays               = 14;
    private const int    MinDataPoints            = 5;
    private const int    MinLateDinners           = 3;
    private const int    MinCorrelationDays       = 3;
    private const double MinSleepReductionMinutes = 30.0;

    private readonly IMealService    _mealService;
    private readonly IHealthProvider _healthProvider;
    private readonly IJournalService _journalService;
    private readonly ITaskService    _taskService;

    public InsightCategory Category => InsightCategory.Health;

    public MealInsightProvider( IMealService    mealService
                              , IHealthProvider healthProvider
                              , IJournalService journalService
                              , ITaskService    taskService )
    {
        _mealService    = mealService    ?? throw new ArgumentNullException(nameof(mealService));
        _healthProvider = healthProvider ?? throw new ArgumentNullException(nameof(healthProvider));
        _journalService = journalService ?? throw new ArgumentNullException(nameof(journalService));
        _taskService    = taskService    ?? throw new ArgumentNullException(nameof(taskService));
    }

    public async IAsyncEnumerable<Insight> GenerateAsync(
        ConversationContext                        context
      , [EnumeratorCancellation] CancellationToken cancellationToken = default )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var today   = DateTimeOffset.Now.Date;
        var fromUtc = new DateTimeOffset(today.AddDays(-WindowDays), TimeSpan.Zero).ToUniversalTime();

        var meals = await _mealService.ListAsync(fromUtc: fromUtc, toUtc: DateTimeOffset.UtcNow).ConfigureAwait(false);
        if (meals.Count < MinDataPoints)
            yield break;

        var mealsByDate = meals.GroupBy(meal => meal.ConsumedAt.ToLocalTime().Date)
                               .ToDictionary(group => group.Key, group => group.ToList());

        if (_healthProvider.IsConnected)
        {
            var sleepByDate = await CollectDailySleepAsync(today, cancellationToken).ConfigureAwait(false);

            var lateDinnerSleep     = new List<double>();
            var normalDinnerSleep   = new List<double>();
            var lateCaffeineSleep   = new List<double>();
            var normalCaffeineSleep = new List<double>();

            foreach (var date in sleepByDate.Keys)
            {
                var sleepMinutes = sleepByDate[date];

                var hasLateDinner = mealsByDate.TryGetValue(date, out var dailyMeals) &&
                                    dailyMeals.Any(meal => meal.MealType == MealType.Dinner && meal.ConsumedAt.ToLocalTime().Hour >= 20);

                if (hasLateDinner)
                    lateDinnerSleep.Add(sleepMinutes);
                else
                    normalDinnerSleep.Add(sleepMinutes);

                var hasLateCaffeine = dailyMeals is not null &&
                                      dailyMeals.Any(meal => meal.ConsumedAt.ToLocalTime().Hour >= 15 && meal.Foods.Any(IsHighCaffeine));

                if (hasLateCaffeine)
                    lateCaffeineSleep.Add(sleepMinutes);
                else
                    normalCaffeineSleep.Add(sleepMinutes);
            }

            if (lateDinnerSleep.Count >= MinLateDinners && normalDinnerSleep.Count > 0)
            {
                var avgLate   = lateDinnerSleep.Average();
                var avgNormal = normalDinnerSleep.Average();

                if (avgNormal - avgLate >= MinSleepReductionMinutes)
                {
                    yield return new Insight
                                 {
                                     Message          = "Over the past 14 days, nights where you logged dinner after 8:00 PM corresponded with noticeably reduced sleep duration. Consider adjusting your meal times for better recovery."
                                   , DeduplicationKey = "health.meals.latedinner"
                                   , Category         = InsightCategory.Health
                                   , Priority         = InsightPriority.Normal
                                   , Reasoning        = new InsightReasoning
                                                       {
                                                           Explanation = $"On nights with dinner after 8:00 PM, recorded sleep averaged {avgLate:F0} minutes compared to {avgNormal:F0} minutes on earlier nights."
                                                       }
                                 };
                }
            }

            if (lateCaffeineSleep.Count >= MinCorrelationDays && normalCaffeineSleep.Count > 0)
            {
                var avgLateCaffeine   = lateCaffeineSleep.Average();
                var avgNormalCaffeine = normalCaffeineSleep.Average();

                if (avgNormalCaffeine - avgLateCaffeine >= MinSleepReductionMinutes)
                {
                    yield return new Insight
                                 {
                                     Message          = "In the past 14 days, sleep duration was noticeably lower on nights following caffeine intake after 3:00 PM. Consider moving coffee or high-caffeine beverages earlier in your day."
                                   , DeduplicationKey = "health.meals.caffeine"
                                   , Category         = InsightCategory.Health
                                   , Priority         = InsightPriority.Normal
                                   , Reasoning        = new InsightReasoning
                                                       {
                                                           Explanation = $"On days with caffeine after 3:00 PM, recorded sleep averaged {avgLateCaffeine:F0} minutes compared to {avgNormalCaffeine:F0} minutes on non-late-caffeine days."
                                                       }
                                 };
                }
            }
        }

        var journals      = _journalService.ListEntries(fromUtc: fromUtc, toUtc: DateTimeOffset.UtcNow);
        var lowMoodByDate = journals.Where(entry => entry.LatestRevision.MoodScore.HasValue && entry.LatestRevision.MoodScore <= 2)
                                    .Select(entry => entry.Entry.CreatedUtc.ToLocalTime().Date)
                                    .ToHashSet();

        var lowMoodWithTriggerDays = 0;
        foreach (var date in lowMoodByDate)
        {
            if (mealsByDate.TryGetValue(date, out var dailyMeals) && dailyMeals.Any(meal => meal.Foods.Any(IsMoodTrigger)))
                lowMoodWithTriggerDays++;
        }

        if (lowMoodByDate.Count >= MinCorrelationDays && lowMoodWithTriggerDays >= MinCorrelationDays && ((double)lowMoodWithTriggerDays / lowMoodByDate.Count) >= 0.6)
        {
            yield return new Insight
                         {
                             Message          = "A drop in journal mood scores (2 or lower) has repeatedly coincided with meals containing high sugar, alcohol, or heavy dietary triggers over the past 2 weeks."
                           , DeduplicationKey = "meal.insight.mood_food_correlation"
                           , Category         = InsightCategory.Health
                           , Priority         = InsightPriority.Normal
                           , Reasoning        = new InsightReasoning
                                               {
                                                   Explanation = $"Low mood was logged on {lowMoodByDate.Count} days, and on {lowMoodWithTriggerDays} of those days high-sugar or alcohol triggers were recorded in dietary logs."
                                               }
                         };
        }

        var completedTasks = _taskService.List(fromUtc: fromUtc, toUtc: DateTimeOffset.UtcNow, includeCompleted: true)
                                         .Where(task => task.CompletedAt.HasValue)
                                         .GroupBy(task => task.CompletedAt!.Value.ToLocalTime().Date)
                                         .ToDictionary(group => group.Key, group => group.Count());

        var highProteinTaskCounts = new List<int>();
        var lowProteinTaskCounts  = new List<int>();

        foreach (var (date, dailyMeals) in mealsByDate)
        {
            var hasHighProteinBreakfast = dailyMeals.Any(meal => meal.MealType == MealType.Breakfast
                                                              && meal.Foods.Any(food => food.Nutrition is not null && food.Nutrition.ProteinGrams >= 20.0));

            var count = completedTasks.TryGetValue(date, out var taskCount) ? taskCount : 0;
            if (hasHighProteinBreakfast)
                highProteinTaskCounts.Add(count);
            else if (dailyMeals.Any(meal => meal.MealType == MealType.Breakfast))
                lowProteinTaskCounts.Add(count);
        }

        if (highProteinTaskCounts.Count >= MinCorrelationDays && lowProteinTaskCounts.Count > 0)
        {
            var avgHighProtein = highProteinTaskCounts.Average();
            var avgLowProtein  = lowProteinTaskCounts.Average();

            if (avgHighProtein - avgLowProtein >= 1.0 || (avgLowProtein > 0 && (avgHighProtein / avgLowProtein) >= 1.25))
            {
                yield return new Insight
                             {
                                 Message          = "Your task completion count is noticeably higher on days you recorded breakfast with at least 20g of protein. Starting your morning with steady protein appears to boost daily focus and output."
                               , DeduplicationKey = "meal.insight.productivity_protein"
                               , Category         = InsightCategory.Tasks
                               , Priority         = InsightPriority.Normal
                               , Reasoning        = new InsightReasoning
                                                   {
                                                       Explanation = $"On days logging >= 20g breakfast protein, completed tasks averaged {avgHighProtein:F1} compared to {avgLowProtein:F1} on lower protein morning logs."
                                                   }
                             };
            }
        }
    }

    private async Task<Dictionary<DateTime, double>> CollectDailySleepAsync(
        DateTime          today
      , CancellationToken cancellationToken )
    {
        var sleepByDay = new Dictionary<DateTime, double>(WindowDays);

        for (var offset = WindowDays; offset >= 1; offset--)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var dayStart = new DateTimeOffset(today.AddDays(-offset), TimeSpan.Zero);
            var result   = await _healthProvider.GetSleepAsync(dayStart, dayStart.AddDays(1), cancellationToken).ConfigureAwait(false);

            if (result is not null && result.TotalMinutes > 0)
                sleepByDay[dayStart.Date] = result.TotalMinutes;
        }

        return sleepByDay;
    }

    private static bool IsHighCaffeine(FoodEntry food)
    {
        var isNameCaffeine = food.Name.HasValue()
                          && (food.Name.ContainsIgnoreCase("coffee")
                           || food.Name.ContainsIgnoreCase("espresso")
                           || food.Name.ContainsIgnoreCase("caffeine")
                           || food.Name.ContainsIgnoreCase("energy drink")
                           || food.Name.ContainsIgnoreCase("tea"));
        
        var isAdditionCaffeine = food.Additions.Any(addition => addition.ContainsIgnoreCase("caffeine")
                                                             || addition.ContainsIgnoreCase("espresso")
                                                             || addition.ContainsIgnoreCase("coffee"));

        return isNameCaffeine || isAdditionCaffeine;
    }

    private static bool IsMoodTrigger(FoodEntry food)
    {
        var isNameTrigger = food.Name.HasValue()
                         && (food.Name.ContainsIgnoreCase("sugar")
                          || food.Name.ContainsIgnoreCase("soda")
                          || food.Name.ContainsIgnoreCase("candy")
                          || food.Name.ContainsIgnoreCase("dessert")
                          || food.Name.ContainsIgnoreCase("alcohol")
                          || food.Name.ContainsIgnoreCase("beer")
                          || food.Name.ContainsIgnoreCase("wine"));

        var isAdditionTrigger = food.Additions.Any(addition => addition.ContainsIgnoreCase("sugar")
                                                            || addition.ContainsIgnoreCase("syrup")
                                                            || addition.ContainsIgnoreCase("alcohol"));

        return isNameTrigger || isAdditionTrigger;
    }
}
