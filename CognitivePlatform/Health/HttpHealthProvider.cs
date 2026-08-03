using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace CognitivePlatform.Api.Health;

public sealed class HttpHealthProvider : IHealthProvider
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<HttpHealthProvider> _logger;

    public HttpHealthProvider(HttpClient httpClient, ILogger<HttpHealthProvider> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<HealthMetricsDto?> GetDailySummaryAsync(DateTime? date = null, CancellationToken cancellationToken = default)
    {
        var targetDate = date ?? DateTime.UtcNow.Date;
        var requestUrl = $"api/health/daily?date={targetDate:yyyy-MM-dd}";

        try
        {
            return await _httpClient.GetFromJsonAsync<HealthMetricsDto>(requestUrl, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to fetch health metrics from bridge for date {Date}.", targetDate);
            return null;
        }
    }

    public async Task<SleepSummaryDto?> GetSleepSummaryAsync(DateTime? date = null, CancellationToken cancellationToken = default)
    {
        var targetDate = date ?? DateTime.UtcNow.Date;
        var requestUrl = $"api/health/sleep?date={targetDate:yyyy-MM-dd}";

        try
        {
            return await _httpClient.GetFromJsonAsync<SleepSummaryDto>(requestUrl, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to fetch sleep summary from bridge for date {Date}.", targetDate);
            return null;
        }
    }
}
