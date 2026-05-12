using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Workspace;

namespace CognitivePlatform.Api.Domains.Activity;

public sealed class ObjectStoreActivityLog : IActivityLog
{
    private readonly IObjectStore      _store;
    private readonly IWorkspaceContext _workspaceContext;

    public ObjectStoreActivityLog(IObjectStore store, IWorkspaceContext workspaceContext)
    {
        _store            = store;
        _workspaceContext = workspaceContext;
    }

    public async Task LogAsync(ActivityEvent activityEvent, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _store.Save(activityEvent
                        , partitionKey: _workspaceContext.ActivePartitionKey
                        , id:           activityEvent.Id)
                    .ConfigureAwait(false);
    }

    public Task<IReadOnlyList<ActivityEvent>> ListAsync( DateTimeOffset?   fromUtc           = null
                                                       , DateTimeOffset?   toUtc             = null
                                                       , CancellationToken cancellationToken = default )
    {
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<ActivityEvent> ordered =
            _store.List<ActivityEvent>(partitionKey: _workspaceContext.ActivePartitionKey
                                     , fromUtc: fromUtc
                                     , toUtc:   toUtc)
                  .OrderByDescending(activityEvent => activityEvent.OccurredUtc)
                  .ToList();

        return Task.FromResult(ordered);
    }
}
