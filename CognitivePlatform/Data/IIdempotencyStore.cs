using CognitivePlatform.Api.Contracts;

namespace CognitivePlatform.Api.Data;

public interface IIdempotencyStore
{
    Task<ConverseResponse?> TryGetAsync( Guid              clientRequestId
                                       , CancellationToken ct );

    Task StoreAsync( Guid              clientRequestId
                   , ConverseResponse  response
                   , CancellationToken ct );
}
