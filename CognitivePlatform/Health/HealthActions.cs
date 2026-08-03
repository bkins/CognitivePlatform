using System.ComponentModel;
using CognitivePlatform.Api.Attributes;
using CognitivePlatform.Api.Execution;

namespace CognitivePlatform.Api.Health;

[Category("Health")]
public sealed class HealthActions
{
    private readonly IHealthProvider _healthProvider;

    public HealthActions(IHealthProvider healthProvider)
    {
        _healthProvider = healthProvider;
    }

    [NaturalLanguageAction(
        Description = "Gets the user's step count for today or a specified date.",
        Examples = new[] { "how many steps did I take today", "get step count for yesterday" }
    )]
    public async Task<ActionResult> GetStepCountAsync(
        [Description("Optional date string (yyyy-MM-dd) or relative keyword (e.g. today, yesterday)")] string? date = null)
    {
        var targetDate = ParseDateOrDefault(date);
        var metrics = await _healthProvider.GetDailySummaryAsync(targetDate);

        if (metrics is null)
        {
            return new ActionResult { Success = true, Message = $"No step count data available for {targetDate:yyyy-MM-dd}. Ensure Health Connect bridge is active." };
        }

        return new ActionResult { Success = true, Message = $"Step count for {targetDate:yyyy-MM-dd}: {metrics.Steps:N0} steps (Distance: {metrics.DistanceKm:F2} km, Calories: {metrics.CaloriesBurned:F0} kcal)." };
    }

    [NaturalLanguageAction(
        Description = "Gets the user's sleep metrics for today or a specified date.",
        Examples = new[] { "how did I sleep last night", "get sleep summary" }
    )]
    public async Task<ActionResult> GetSleepDataAsync(
        [Description("Optional date string (yyyy-MM-dd) or relative keyword (e.g. today, yesterday)")] string? date = null)
    {
        var targetDate = ParseDateOrDefault(date);
        var sleep = await _healthProvider.GetSleepSummaryAsync(targetDate);

        if (sleep is null)
        {
            return new ActionResult { Success = true, Message = $"No sleep data available for {targetDate:yyyy-MM-dd}. Ensure Health Connect bridge is active." };
        }

        return new ActionResult { Success = true, Message = $"Sleep summary for {targetDate:yyyy-MM-dd}: Total {sleep.TotalSleepHours:F1} hrs (Deep: {sleep.DeepSleepHours:F1} hrs, REM: {sleep.RemSleepHours:F1} hrs, Light: {sleep.LightSleepHours:F1} hrs)." };
    }

    private static DateTime ParseDateOrDefault(string? date)
    {
        if (string.IsNullOrWhiteSpace(date))
        {
            return DateTime.UtcNow.Date;
        }

        if (date.Equals("yesterday", StringComparison.OrdinalIgnoreCase))
        {
            return DateTime.UtcNow.Date.AddDays(-1);
        }

        if (DateTime.TryParse(date, out var parsed))
        {
            return parsed.Date;
        }

        return DateTime.UtcNow.Date;
    }
}
