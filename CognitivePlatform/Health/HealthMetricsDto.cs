namespace CognitivePlatform.Api.Health;

public sealed record HealthMetricsDto
{
    public int Steps { get; init; }
    public double DistanceKm { get; init; }
    public double CaloriesBurned { get; init; }
    public double AverageHeartRate { get; init; }
    public DateTime Date { get; init; } = DateTime.UtcNow.Date;
}

public sealed record SleepSummaryDto
{
    public double TotalSleepHours { get; init; }
    public double DeepSleepHours { get; init; }
    public double LightSleepHours { get; init; }
    public double RemSleepHours { get; init; }
    public DateTime Date { get; init; } = DateTime.UtcNow.Date;
}
