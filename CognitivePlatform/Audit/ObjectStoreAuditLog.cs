using CognitivePlatform.Api.Data;

namespace CognitivePlatform.Api.Audit;

/// <summary>
/// Persists audit events in the universal object store.
/// Uses GetAwaiter().GetResult() to satisfy the synchronous IAuditLog.Append
/// contract from within ExecutionEngine.Execute (which is currently synchronous).
///
/// When ExecutionEngine is made async (see DEFERRED.md), replace with a proper
/// async AppendAsync and propagate the await up.
/// </summary>
public sealed class ObjectStoreAuditLog : IAuditLog
{
    private readonly IObjectStore _store;

    public ObjectStoreAuditLog(IObjectStore store)
    {
        _store = store;
    }

    public void Append(AuditEvent auditEvent)
    {
        _store.Save(auditEvent, partitionKey: null, id: auditEvent.Id)
              .GetAwaiter()
              .GetResult();
    }

    public IReadOnlyList<AuditEvent> List( DateTimeOffset? fromUtc = null
                                         , DateTimeOffset? toUtc   = null )
    {
        return _store.List<AuditEvent>(partitionKey: null, fromUtc: fromUtc, toUtc: toUtc)
                     .OrderByDescending(e => e.OccurredUtc)
                     .ToList();
    }
}
