using System.Text.Json;
using CP.Shared.Primitives.Avails.Extensions;

namespace CognitivePlatform.Admin.CpAdminClients;

public sealed class AdminRegistryClient : IAdminRegistryClient
{
    private readonly HttpClient _http;

    private static readonly JsonSerializerOptions CaseInsensitive =
        new() { PropertyNameCaseInsensitive = true };

    public AdminRegistryClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<IReadOnlyList<ActionMetadataDto>> GetActionsAsync(CancellationToken ct = default)
    {
        var result = await _http.GetFromJsonAsync<List<ActionMetadataDto>>("api/admin/registry", ct);

        return result ?? [];
    }

    public async Task<ConverseResultDto?> TestInvokeAsync(string input, CancellationToken ct = default)
    {
        var payload  = new { Input = input };
        var response = await _http.PostAsJsonAsync("api/conversation/converse", payload, ct);

        if (response.IsSuccessStatusCode.Not())
        {
            // Try to surface the actual error message from the response body rather
            // than just showing the HTTP status code number to the admin.
            try
            {
                var body = await response.Content.ReadFromJsonAsync<ConverseResultDto>(CaseInsensitive, ct);
                if (body is not null && body.Message.HasValue())
                    return body with { Success = false };
            }
            catch { /* fall through to status-code fallback */ }

            return new ConverseResultDto { Success = false, Message = $"HTTP {(int)response.StatusCode}" };
        }

        return await response.Content.ReadFromJsonAsync<ConverseResultDto>(CaseInsensitive, ct);
    }
}
