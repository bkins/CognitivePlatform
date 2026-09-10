using System.Net.Http.Json;

namespace CognitivePlatform.Admin.CpAdminClients;

public sealed class AdminTrustTraceClient : IAdminTrustTraceClient
{
    private readonly HttpClient _httpClient;

    public AdminTrustTraceClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<TrustTraceDto>> GetRecentAsync(CancellationToken cancellationToken = default)
    {
        return await _httpClient.GetFromJsonAsync<List<TrustTraceDto>>("api/admin/trust-traces", cancellationToken) ?? [];
    }
}
