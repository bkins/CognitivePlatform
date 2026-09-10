using System.Net.Http.Json;

namespace CognitivePlatform.Admin.CpAdminClients;

public sealed class AdminActionHistoryClient : IAdminActionHistoryClient
{
    private readonly HttpClient _httpClient;

    public AdminActionHistoryClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<ActionHistoryItemDto>> GetHistoryAsync(string? outcome = null, CancellationToken cancellationToken = default)
    {
        var route = outcome is null ? "api/admin/action-history" : $"api/admin/action-history?outcome={Uri.EscapeDataString(outcome)}";
        return await _httpClient.GetFromJsonAsync<List<ActionHistoryItemDto>>(route, cancellationToken) ?? [];
    }
}
