using System.Runtime.CompilerServices;
using CognitivePlatform.Api.Conversation;
using CognitivePlatform.Api.Domains.Journal.Interfaces;
using CognitivePlatform.Api.Insights.Models;
using CognitivePlatform.Api.Integrations.Health;

namespace CognitivePlatform.Api.Insights;

/// <summary>
/// Phase A (ENH-31) insight provider. Correlates daily sleep duration from Health Connect
/// with mood scores from journal entries over a rolling 14-day window. Surfaces a finding
/// only when the Pearson correlation coefficient r ≥ 0.4 and at least 5 paired data points
/// exist (days with both sleep data and a mood-scored journal entry).
///
/// <para>
/// Trigger:          Pearson r ≥ 0.4 across ≥ 5 days with both sleep data and mood score.
/// Message:          Advisory sleep–mood correlation observation.
/// DeduplicationKey: health.correlation
/// RepeatWindow:     72 hours (enforced by InsightPolicy via the engine).
/// InsightCategory:  Health
/// </para>
/// </summary>
public sealed class HealthCorrelationInsightProvider : IInsightProvider
{
    private const int    WindowDays    = 14;
    private const int    MinDataPoints = 5;
    private const double MinPearsonR   = 0.4;

    private readonly IHealthProvider _healthProvider;
    private readonly IJournalService _journalService;

    public InsightCategory Category => InsightCategory.Health;

    public HealthCorrelationInsightProvider( IHealthProvider  healthProvider
                                           , IJournalService  journalService )
    {
        _healthProvider = healthProvider ?? throw new ArgumentNullException(nameof(healthProvider));
        _journalService = journalService ?? throw new ArgumentNullException(nameof(journalService));
    }

    public async IAsyncEnumerable<Insight> GenerateAsync(
        ConversationContext                        context
      , [EnumeratorCancellation] CancellationToken cancellationToken = default )
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_healthProvider.IsConnected)
            yield break;

        var today   = DateTimeOffset.UtcNow.Date;
        var fromUtc = new DateTimeOffset(today.AddDays(-WindowDays), TimeSpan.Zero);

        var sleepByDay = await CollectDailySleepAsync(today, cancellationToken);

        if (sleepByDay.Count == 0)
            yield break;

        var moodByDay = _journalService
            .ListEntries(fromUtc: fromUtc)
            .Where(entry => entry.LatestRevision.MoodScore.HasValue)
            .GroupBy(entry => entry.Entry.CreatedUtc.UtcDateTime.Date)
            .ToDictionary(group => group.Key
                        , group => group.Average(entry => (double)entry.LatestRevision.MoodScore!.Value));

        var pairs = sleepByDay.Keys
            .Where(day => moodByDay.ContainsKey(day))
            .Select(day => (Sleep: sleepByDay[day], Mood: moodByDay[day]))
            .ToList();

        if (pairs.Count < MinDataPoints)
            yield break;

        var r = ComputePearsonR(
            pairs.Select(pair => pair.Sleep).ToList()
          , pairs.Select(pair => pair.Mood).ToList());

        if (r < MinPearsonR)
            yield break;

        yield return new Insight
                     {
                             Message          = "Over the past 14 days your sleep and mood have moved together — on days you slept more your recorded mood tended to be higher. Worth keeping in mind."
                           , DeduplicationKey = "health.correlation"
                           , Category         = InsightCategory.Health
                           , Priority         = InsightPriority.Normal
                           , Reasoning        = new InsightReasoning
                                               {
                                                       Explanation = $"Sleep–mood Pearson r = {r:F2} across {pairs.Count} paired days in the 14-day window."
                                               }
                     };
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
            var result   = await _healthProvider.GetSleepAsync(dayStart, dayStart.AddDays(1), cancellationToken);

            if (result.TotalMinutes > 0)
                sleepByDay[dayStart.DateTime] = result.TotalMinutes;
        }

        return sleepByDay;
    }

    private static double ComputePearsonR(IReadOnlyList<double> xs, IReadOnlyList<double> ys)
    {
        var n    = xs.Count;
        var xBar = xs.Average();
        var yBar = ys.Average();

        double numerator = 0.0;
        double xSumSq    = 0.0;
        double ySumSq    = 0.0;

        for (var i = 0; i < n; i++)
        {
            var dx  = xs[i] - xBar;
            var dy  = ys[i] - yBar;
            numerator += dx * dy;
            xSumSq    += dx * dx;
            ySumSq    += dy * dy;
        }

        var denominator = Math.Sqrt(xSumSq * ySumSq);
        return denominator < double.Epsilon ? 0.0 : numerator / denominator;
    }
}
