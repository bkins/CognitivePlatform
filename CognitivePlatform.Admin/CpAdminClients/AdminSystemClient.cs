using System.Net.Http.Json;

namespace CognitivePlatform.Admin.CpAdminClients;

public sealed class AdminSystemClient : IAdminSystemClient
{
    private readonly HttpClient _http;

    public AdminSystemClient(HttpClient http)
    {
        _http = http;
    }

    public Task<SystemStatsResponse?> GetStatsAsync(CancellationToken ct = default)
        => _http.GetFromJsonAsync<SystemStatsResponse>("api/admin/system/stats", ct);

    public async Task<IReadOnlyList<TelemetryMetricsDto>> GetTelemetryMetricsAsync(CancellationToken ct = default)
    {
        var result = await _http.GetFromJsonAsync<List<TelemetryMetricsDto>>("api/system/telemetry", ct);
        return result ?? [];
    }
}
