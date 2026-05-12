namespace CognitivePlatform.Api.Workspace;

/// <summary>
/// Ambient workspace state for the running server instance.
///
/// <para>
/// The active workspace is the default partition key used by all domain
/// services (Tasks, Journal, Activities, DailyRecord) for reads and writes.
/// It persists across restarts via <see cref="IUserSettingsService"/>.
/// </para>
///
/// <para>
/// Registered as a singleton. Thread-safe: reads are non-blocking; writes
/// update both the in-memory value and the persisted settings atomically
/// (best-effort — in-memory is always consistent; persistence is async).
/// </para>
/// </summary>
public interface IWorkspaceContext
{
    /// <summary>The currently active workspace name (e.g. "personal", "work").</summary>
    string ActiveWorkspace { get; }

    /// <summary>
    /// Translates the active workspace name to the IObjectStore <c>partitionKey</c>
    /// parameter. "personal" maps to <c>null</c>; all other names pass through as-is.
    /// </summary>
    string? ActivePartitionKey { get; }

    /// <summary>
    /// All workspace names that have ever been activated on this installation.
    /// "personal" is always included. Updated in-memory immediately when
    /// <see cref="SetActiveWorkspaceAsync"/> is called with a new name.
    /// </summary>
    IReadOnlyList<string> KnownWorkspaces { get; }

    /// <summary>
    /// Sets the active workspace to <paramref name="workspaceName"/> and persists
    /// the change to <see cref="IUserSettingsService"/> so it survives restarts.
    /// The name is normalised to lower-case and trimmed before storing.
    /// If the name is new, it is also appended to <see cref="KnownWorkspaces"/>.
    /// </summary>
    Task SetActiveWorkspaceAsync(string workspaceName);
}
