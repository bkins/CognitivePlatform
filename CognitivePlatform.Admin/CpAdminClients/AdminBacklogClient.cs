using System.Net.Http.Json;
using CognitivePlatform.Api.Domains.Backlog;

namespace CognitivePlatform.Admin.CpAdminClients;

public sealed class AdminBacklogClient : IAdminBacklogClient
{
    private readonly HttpClient _http;

    public AdminBacklogClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<BacklogBoardDto> GetBoardAsync(CancellationToken cancellationToken = default)
    {
        return await _http.GetFromJsonAsync<BacklogBoardDto>("api/admin/backlog/board", cancellationToken)
               ?? throw new InvalidOperationException("The Backlog API returned no board data.");
    }

    public async Task<BacklogStoryDto> CreateStoryAsync(CreateBacklogStoryRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _http.PostAsJsonAsync("api/admin/backlog/stories", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<BacklogStoryDto>(cancellationToken)
               ?? throw new InvalidOperationException("The Backlog API returned no created story.");
    }

    public async Task<BacklogStoryDto> UpdateStoryAsync(Guid storyId, UpdateBacklogStoryRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _http.PatchAsJsonAsync($"api/admin/backlog/stories/{storyId}", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<BacklogStoryDto>(cancellationToken)
               ?? throw new InvalidOperationException("The Backlog API returned no updated story.");
    }

    public async Task<BacklogStoryDto> MoveStoryAsync(Guid storyId, MoveBacklogStoryRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _http.PostAsJsonAsync($"api/admin/backlog/stories/{storyId}/move", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<BacklogStoryDto>(cancellationToken)
               ?? throw new InvalidOperationException("The Backlog API returned no moved story.");
    }

    public async Task<BacklogStoryDto> ArchiveStoryAsync(Guid storyId, ArchiveBacklogStoryRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _http.PostAsJsonAsync($"api/admin/backlog/stories/{storyId}/archive", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<BacklogStoryDto>(cancellationToken)
               ?? throw new InvalidOperationException("The Backlog API returned no archived story.");
    }

    public async Task<BacklogStoryDto> UnarchiveStoryAsync(Guid storyId, CancellationToken cancellationToken = default)
    {
        var response = await _http.PostAsync($"api/admin/backlog/stories/{storyId}/unarchive", null, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<BacklogStoryDto>(cancellationToken)
               ?? throw new InvalidOperationException("The Backlog API returned no unarchived story.");
    }

    public async Task<IReadOnlyList<BacklogStoryDto>> GetArchivedStoriesAsync(CancellationToken cancellationToken = default)
    {
        return await _http.GetFromJsonAsync<List<BacklogStoryDto>>("api/admin/backlog/stories/archived", cancellationToken)
               ?? new List<BacklogStoryDto>();
    }

    public async Task<BacklogReferenceDto> CreateReferenceAsync(string referenceType, CreateBacklogReferenceRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _http.PostAsJsonAsync($"api/admin/backlog/references/{referenceType}", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<BacklogReferenceDto>(cancellationToken)
               ?? throw new InvalidOperationException("The Backlog API returned no created reference.");
    }

    public async Task<BacklogReferenceDto> UpdateReferenceAsync(string referenceType, string key, UpdateBacklogReferenceRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _http.PatchAsJsonAsync($"api/admin/backlog/references/{referenceType}/{Uri.EscapeDataString(key)}", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<BacklogReferenceDto>(cancellationToken)
               ?? throw new InvalidOperationException("The Backlog API returned no updated reference.");
    }

    public async Task<IReadOnlyList<BacklogIntakeDto>> GetIntakeAsync(CancellationToken cancellationToken = default)
    {
        return await _http.GetFromJsonAsync<List<BacklogIntakeDto>>("api/admin/backlog/intake", cancellationToken)
               ?? new List<BacklogIntakeDto>();
    }

    public async Task<IReadOnlyList<BacklogAuditEventDto>> GetHistoryAsync(Guid storyId, CancellationToken cancellationToken = default)
    {
        return await _http.GetFromJsonAsync<List<BacklogAuditEventDto>>($"api/admin/backlog/stories/{storyId}/history", cancellationToken)
               ?? new List<BacklogAuditEventDto>();
    }

    public async Task<string> ExportMarkdownAsync(CancellationToken cancellationToken = default)
    {
        var response = await _http.PostAsync("api/admin/backlog/export/markdown", null, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }
}
