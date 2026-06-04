using System.Text.Json.Serialization;

namespace CognitivePlatform.Admin.CpAdminClients;

// ── DTOs ──────────────────────────────────────────────────────────────────────

public sealed class TrainingCorpusStatsDto
{
    [JsonPropertyName("totalRecords")]   public int    TotalRecords   { get; init; }
    [JsonPropertyName("succeededCount")] public int    SucceededCount { get; init; }
    [JsonPropertyName("successRate")]    public double SuccessRate    { get; init; }
    [JsonPropertyName("avgLatencyMs")]   public double AvgLatencyMs   { get; init; }

    [JsonPropertyName("dailyCounts")]
    public List<DailyCountDto> DailyCounts { get; init; } = [];

    [JsonPropertyName("actionCounts")]
    public List<ActionCountDto> ActionCounts { get; init; } = [];
}

public sealed class DailyCountDto
{
    [JsonPropertyName("day")]         public string Day         { get; init; } = string.Empty;
    [JsonPropertyName("recordCount")] public int    RecordCount { get; init; }
}

public sealed class ActionCountDto
{
    [JsonPropertyName("action")]      public string Action      { get; init; } = string.Empty;
    [JsonPropertyName("recordCount")] public int    RecordCount { get; init; }
}

// ── Interface ─────────────────────────────────────────────────────────────────

public interface IAdminTrainingClient
{
    Task<TrainingCorpusStatsDto?> GetStatsAsync(CancellationToken ct = default);
}

// ── Client ────────────────────────────────────────────────────────────────────

public sealed class AdminTrainingClient : IAdminTrainingClient
{
    private readonly HttpClient _http;

    public AdminTrainingClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<TrainingCorpusStatsDto?> GetStatsAsync(CancellationToken ct = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<TrainingCorpusStatsDto>("api/admin/training/corpus/stats", ct);
        }
        catch
        {
            return null;
        }
    }
}
