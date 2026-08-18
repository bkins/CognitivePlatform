using CognitivePlatform.Api.Data;

namespace CognitivePlatform.Api.Audit;

public sealed class ObjectStoreAuditLog : IAuditLog
{
    private readonly IObjectStore _store;

    public ObjectStoreAuditLog(IObjectStore store)
    {
        _store = store;
    }

    public async Task AppendAsync(AuditEvent auditEvent)
    {
        await _store.Save(auditEvent
                        , partitionKey: null,
                          id: auditEvent.Id)
                    .ConfigureAwait(false);
    }

    public IReadOnlyList<AuditEvent> List( DateTimeOffset? fromUtc = null
                                         , DateTimeOffset? toUtc   = null )
    {
        return _store.List<AuditEvent>(partitionKey: null, fromUtc: fromUtc, toUtc: toUtc)
                     .OrderByDescending(e => e.OccurredUtc)
                     .ToList();
    }
}
