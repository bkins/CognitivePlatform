namespace CognitivePlatform.Api.Domains.Activity;

/// <summary>
/// Append-only log of explicit activity/habit events, mirroring <see cref="Api.Audit.IAuditLog"/>.
/// Edit and delete are out of scope — events are a historical signal, not mutable state.
/// </summary>
public interface IActivityLog
{
    /// <summary>Records one activity event asynchronously.</summary>
    Task LogAsync(ActivityEvent activityEvent, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists activity events in descending chronological order.
    /// Both bounds are inclusive and optional.
    /// </summary>
    Task<IReadOnlyList<ActivityEvent>> ListAsync( DateTimeOffset?   fromUtc           = null
                                                , DateTimeOffset?   toUtc             = null
                                                , CancellationToken cancellationToken = default );
}
