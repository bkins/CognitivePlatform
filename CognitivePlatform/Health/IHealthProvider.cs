namespace CognitivePlatform.Api.Health;

public interface IHealthProvider
{
    Task<HealthMetricsDto?> GetDailySummaryAsync(DateTime? date = null, CancellationToken cancellationToken = default);
    Task<SleepSummaryDto?> GetSleepSummaryAsync(DateTime? date = null, CancellationToken cancellationToken = default);
}
