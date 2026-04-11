namespace CognitivePlatform.Api.Audit;

/// <summary>
/// A single immutable record of an action execution.
/// Stored append-only in the object store — never modified after creation.
///
/// Design intent: keep fields minimal now; add to Meta for anything
/// domain-specific so the schema stays stable. When a structured UI
/// or reporting layer is needed, promote Meta fields to first-class
/// properties (see DEFERRED.md — Audit Log Evolution).
/// </summary>
public sealed class AuditEvent
{
    public string          Id          { get; init; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset  OccurredUtc { get; init; } = DateTimeOffset.UtcNow;
    public string          ActionName  { get; init; } = string.Empty;

    /// <summary>
    /// Serialized action parameters as "key=value, key=value".
    /// Kept as a string to avoid schema coupling with specific action types.
    /// </summary>
    public string          Parameters  { get; init; } = string.Empty;

    public AuditOutcome    Outcome     { get; init; }

    /// <summary>Populated on failure; null on success.</summary>
    public string?         ErrorMessage { get; init; }

    /// <summary>
    /// Extensible bag for future fields (e.g. UserId, SessionId, IpAddress).
    /// Promote to first-class properties when a reporting layer is added.
    /// </summary>
    public Dictionary<string, string> Meta { get; init; } = new();
}

public enum AuditOutcome
{
    Success,
    Failure
}
