using CognitivePlatform.Api.Data;

namespace CognitivePlatform.Api.Domains.Activity;

public sealed class ObjectStoreActivityLog : IActivityLog
{
    private readonly IObjectStore _store;

    public ObjectStoreActivityLog(IObjectStore store)
    {
        _store = store;
    }

    public async Task LogAsync(ActivityEvent activityEvent, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _store.Save(activityEvent, partitionKey: null, id: activityEvent.Id).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<ActivityEvent>> ListAsync( DateTimeOffset?   fromUtc           = null
                                                       , DateTimeOffset?   toUtc             = null
                                                       , CancellationToken cancellationToken = default )
    {
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<ActivityEvent> ordered = _store.List<ActivityEvent>(partitionKey: null, fromUtc: fromUtc, toUtc: toUtc)
                                                     .OrderByDescending(activityEvent => activityEvent.OccurredUtc)
                                                     .ToList();

        return Task.FromResult(ordered);
    }
}
