namespace CognitivePlatform.Admin.CpAdminClients;

/// <summary>
/// Targets GET /api/admin/logs and DELETE /api/admin/logs.
/// </summary>
public interface IAdminLogsClient
{
    Task<IReadOnlyList<LogEntryDto>> GetLogsAsync( int     take   = 200
                                                 , string? level  = null
                                                 , string? search = null
                                                 , CancellationToken ct = default);

    Task ClearAsync(CancellationToken ct = default);
}
