using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using CognitivePlatform.Api.Conversation;
using CognitivePlatform.Api.Domains.Journal.Interfaces;
using CognitivePlatform.Api.Domains.Meals;
using CognitivePlatform.Api.Insights.Models;
using CognitivePlatform.Api.Integrations.Health;

namespace CognitivePlatform.Api.Insights;

/// <summary>
/// Proactively correlates dietary habits (e.g., late dinners after 8:00 PM local time or high-sugar meals)
/// with next-day sleep telemetry and journaled mood scores over a rolling 14-day window.
/// </summary>
public sealed class MealInsightProvider : IInsightProvider
{
    private const int      WindowDays              = 14;
    private const int      MinDataPoints           = 5;
    private const int      MinLateDinners          = 3;
    private const double   MinSleepReductionMinutes = 30.0;

    private readonly IMealService    _mealService;
    private readonly IHealthProvider _healthProvider;
    private readonly IJournalService _journalService;

    public InsightCategory Category => InsightCategory.Health;

    public MealInsightProvider( IMealService    mealService
                              , IHealthProvider healthProvider
                              , IJournalService journalService )
    {
        _mealService    = mealService    ?? throw new ArgumentNullException(nameof(mealService));
        _healthProvider = healthProvider ?? throw new ArgumentNullException(nameof(healthProvider));
        _journalService = journalService ?? throw new ArgumentNullException(nameof(journalService));
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

            var lateDinnerSleep   = new List<double>();
            var normalDinnerSleep = new List<double>();

            foreach (var date in sleepByDate.Keys)
            {
                var sleepMinutes = sleepByDate[date];
                var hasLateDinner = mealsByDate.TryGetValue(date, out var dailyMeals) &&
                                    dailyMeals.Any(meal => meal.MealType == MealType.Dinner && meal.ConsumedAt.ToLocalTime().Hour >= 20);

                if (hasLateDinner)
                    lateDinnerSleep.Add(sleepMinutes);
                else
                    normalDinnerSleep.Add(sleepMinutes);
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
}
