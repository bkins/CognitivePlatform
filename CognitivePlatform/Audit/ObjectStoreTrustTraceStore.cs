using CognitivePlatform.Api.Contracts;
using CognitivePlatform.Api.Data;

namespace CognitivePlatform.Api.Audit;

public sealed class ObjectStoreTrustTraceStore : ITrustTraceStore
{
    private const string PartitionKey = "trust-traces";

    private readonly IObjectStore _store;

    public ObjectStoreTrustTraceStore(IObjectStore store)
    {
        _store = store;
    }

    public async Task<string> RecordAsync( string                        actionName
                                          , bool                          success
                                          , IReadOnlyList<TransparencyItem> items
                                          , CancellationToken             cancellationToken = default )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var trace = new TrustTrace
                    {
                            ActionName = actionName
                          , Success    = success
                          , Items      = items
                    };
        return await _store.Save(trace, partitionKey: PartitionKey, id: trace.Id).ConfigureAwait(false);
    }

    public IReadOnlyList<TrustTrace> List(int take)
    {
        return _store.List<TrustTrace>(partitionKey: PartitionKey)
                     .OrderByDescending(trace => trace.OccurredUtc)
                     .Take(Math.Clamp(take, 1, 200))
                     .ToList();
    }
}
