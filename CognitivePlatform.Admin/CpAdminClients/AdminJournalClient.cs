using CP.Shared.Primitives.Avails.Extensions;

namespace CognitivePlatform.Admin.CpAdminClients;

public sealed class AdminJournalClient : IAdminJournalClient
{
    private readonly HttpClient _http;

    public AdminJournalClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<IReadOnlyList<JournalEntryAdminDto>> GetEntriesAsync(CancellationToken ct = default)
    {
        var result = await _http.GetFromJsonAsync<List<JournalEntryAdminDto>>("api/admin/journal/entries", ct);
        
        return result ?? [];
    }

    public async Task<IReadOnlyList<JournalRevisionAdminDto>> GetRevisionsAsync( string            entryId
                                                                               , CancellationToken ct = default)
    {
        var result = await _http.GetFromJsonAsync<List<JournalRevisionAdminDto>>(
            $"api/admin/journal/entries/{Uri.EscapeDataString(entryId)}/revisions", ct);
        return result ?? [];
    }

    public async Task<string?> AddCorrectionAsync( string                     entryId
                                                 , AddCorrectionRevisionRequest request
                                                 , CancellationToken            ct = default)
    {
        var response = await _http.PostAsJsonAsync(
            $"api/admin/journal/entries/{Uri.EscapeDataString(entryId)}/revisions"
          , request
          , ct);

        if (response.IsSuccessStatusCode.Not()) return null;

        var result = await response.Content.ReadFromJsonAsync<CorrectionResultDto>(cancellationToken: ct);
        
        return result?.RevisionId;
    }

    public async Task<RepairPartitionKeysResultDto?> RepairPartitionKeysAsync(CancellationToken ct = default)
    {
        var response = await _http.PostAsync("api/admin/journal/repair-partition-keys", content: null, ct);

        if (response.IsSuccessStatusCode.Not()) return null;

        return await response.Content.ReadFromJsonAsync<RepairPartitionKeysResultDto>(cancellationToken: ct);
    }

    private sealed record CorrectionResultDto { public string? RevisionId { get; init; } }
}
