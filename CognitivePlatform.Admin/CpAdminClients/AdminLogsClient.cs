using CP.Shared.Primitives.Avails.Extensions;

namespace CognitivePlatform.Admin.CpAdminClients;

public sealed class AdminLogsClient : IAdminLogsClient
{
    private readonly HttpClient _http;

    public AdminLogsClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<IReadOnlyList<LogEntryDto>> GetLogsAsync( int     take   = 200
                                                              , string? level  = null
                                                              , string? search = null
                                                              , CancellationToken ct = default)
    {
        var url = $"api/admin/logs?take={take}";

        if (string.IsNullOrWhiteSpace(level).Not())
            url += $"&level={Uri.EscapeDataString(level!)}";

        if (string.IsNullOrWhiteSpace(search).Not())
            url += $"&search={Uri.EscapeDataString(search!)}";

        var result = await _http.GetFromJsonAsync<List<LogEntryDto>>(url, ct);
        return result ?? [];
    }

    public async Task ClearAsync(CancellationToken ct = default)
    {
        await _http.DeleteAsync("api/admin/logs", ct);
    }
}
