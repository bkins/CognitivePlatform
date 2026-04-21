using CP.Shared.Primitives.Avails.Extensions;

namespace CognitivePlatform.Admin.CpAdminClients;

public sealed class AdminKnowledgeClient : IAdminKnowledgeClient
{
    private readonly HttpClient _http;

    public AdminKnowledgeClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<IReadOnlyList<KnowledgeItemAdminDto>> GetAllAsync(CancellationToken ct = default)
    {
        var result = await _http.GetFromJsonAsync<List<KnowledgeItemAdminDto>>("api/admin/knowledge", ct);
        
        return result ?? [];
    }

    public async Task<bool> HardDeleteAsync(string id, CancellationToken ct = default)
    {
        var response = await _http.DeleteAsync($"api/admin/knowledge/{Uri.EscapeDataString(id)}", ct);
        
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> RestoreAsync(string id, CancellationToken ct = default)
    {
        var response = await _http.PostAsync($"api/admin/knowledge/{Uri.EscapeDataString(id)}/restore"
                                           , content: null
                                           , ct);
        return response.IsSuccessStatusCode;
    }

    public async Task<string?> InjectAsync(InjectKnowledgeRequest request, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("api/admin/knowledge", request, ct);
        if (response.IsSuccessStatusCode.Not()) return null;

        var result = await response.Content.ReadFromJsonAsync<InjectResultDto>(cancellationToken: ct);
        
        return result?.Id;
    }

    private sealed record InjectResultDto { public string? Id { get; init; } }
}
