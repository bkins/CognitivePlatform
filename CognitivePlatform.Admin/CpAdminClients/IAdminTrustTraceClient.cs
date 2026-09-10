namespace CognitivePlatform.Admin.CpAdminClients;

public interface IAdminTrustTraceClient
{
    Task<IReadOnlyList<TrustTraceDto>> GetRecentAsync(CancellationToken cancellationToken = default);
}
