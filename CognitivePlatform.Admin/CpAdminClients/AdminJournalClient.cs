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

    public async Task<string?> CreateEntryAsync( CreateJournalEntryAdminDto request
                                                , CancellationToken          ct = default)
    {
        var response = await _http.PostAsJsonAsync("api/admin/journal/entries", request, ct);
        if (response.IsSuccessStatusCode.Not()) return null;

        var result = await response.Content.ReadFromJsonAsync<CreateEntryResultDto>(cancellationToken: ct);
        return result?.EntryId;
    }

    public async Task<bool> SoftDeleteEntryAsync( string                     entryId
                                                 , SoftDeleteJournalAdminDto? request = null
                                                 , CancellationToken          ct      = default)
    {
        var requestMessage = new HttpRequestMessage(HttpMethod.Delete, $"api/admin/journal/entries/{Uri.EscapeDataString(entryId)}")
        {
            Content = JsonContent.Create(request ?? new SoftDeleteJournalAdminDto())
        };

        var response = await _http.SendAsync(requestMessage, ct);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> RestoreEntryAsync( string            entryId
                                              , CancellationToken ct = default)
    {
        var response = await _http.PostAsync($"api/admin/journal/entries/{Uri.EscapeDataString(entryId)}/restore", content: null, ct);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> HardDeleteEntryAsync( string            entryId
                                                , CancellationToken ct = default)
    {
        var response = await _http.DeleteAsync($"api/admin/journal/entries/{Uri.EscapeDataString(entryId)}/hard", ct);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> UpdateRevisionAsync( string                        entryId
                                               , string                        revisionId
                                               , UpdateJournalRevisionAdminDto request
                                               , CancellationToken             ct = default)
    {
        var response = await _http.PutAsJsonAsync(
            $"api/admin/journal/entries/{Uri.EscapeDataString(entryId)}/revisions/{Uri.EscapeDataString(revisionId)}"
          , request
          , ct);

        return response.IsSuccessStatusCode;
    }

    public async Task<RepairPartitionKeysResultDto?> RepairPartitionKeysAsync(CancellationToken ct = default)
    {
        var response = await _http.PostAsync("api/admin/journal/repair-partition-keys", content: null, ct);

        if (response.IsSuccessStatusCode.Not()) return null;

        return await response.Content.ReadFromJsonAsync<RepairPartitionKeysResultDto>(cancellationToken: ct);
    }

    private sealed record CorrectionResultDto { public string? RevisionId { get; init; } }
    private sealed record CreateEntryResultDto { public string? EntryId { get; init; } public string? RevisionId { get; init; } }
}
