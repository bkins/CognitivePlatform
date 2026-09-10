using CognitivePlatform.Api.Contracts;

namespace CognitivePlatform.Api.Audit;

public interface ITrustTraceStore
{
    Task<string> RecordAsync( string                        actionName
                            , bool                          success
                            , IReadOnlyList<TransparencyItem> items
                            , CancellationToken             cancellationToken = default );

    IReadOnlyList<TrustTrace> List(int take);
}
