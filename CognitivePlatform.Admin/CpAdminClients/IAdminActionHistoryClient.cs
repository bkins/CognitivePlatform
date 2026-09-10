namespace CognitivePlatform.Admin.CpAdminClients;

public interface IAdminActionHistoryClient
{
    Task<IReadOnlyList<ActionHistoryItemDto>> GetHistoryAsync(string? outcome = null, CancellationToken cancellationToken = default);
}
