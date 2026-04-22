namespace CognitivePlatform.Api.Audit;

/// <summary>
/// Append-only audit log. Every action execution (success or failure)
/// results in exactly one AuditEvent being written here.
///
/// Future evolution (see DEFERRED.md):
/// - Add filtering by ActionName, Outcome, SessionId
/// - Add /audit/list HTTP endpoint
/// - Add retention / rotation policy
/// </summary>
public interface IAuditLog
{
    /// <summary>Records one audit event asynchronously.</summary>
    Task AppendAsync(AuditEvent auditEvent);

    /// <summary>
    /// Lists audit events in descending chronological order.
    /// Both bounds are inclusive and optional.
    /// </summary>
    IReadOnlyList<AuditEvent> List( DateTimeOffset? fromUtc = null
                                  , DateTimeOffset? toUtc   = null );
}
