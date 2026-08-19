namespace CognitivePlatform.Admin.CpAdminClients;

/// <summary>
/// Targets GET /api/admin/system/* — environment info, LLM config,
/// Groq usage, and per-type object store counts.
/// </summary>
public interface IAdminSystemClient
{
    Task<SystemStatsResponse?> GetStatsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<TelemetryMetricsDto>> GetTelemetryMetricsAsync(CancellationToken ct = default);
}
