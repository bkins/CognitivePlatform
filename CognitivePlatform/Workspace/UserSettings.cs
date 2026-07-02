namespace CognitivePlatform.Api.Workspace;

/// <summary>
/// Persisted user-level settings, stored as a singleton blob in IObjectStore.
/// All properties have safe defaults so a missing record produces a fully
/// functional settings object without throwing.
/// </summary>
public sealed record UserSettings
{
    /// <summary>
    /// Fixed identity key — always "user-settings". The object store uses this
    /// so there is always exactly one UserSettings record per installation.
    /// </summary>
    public string Id { get; init; } = UserSettingsService.SettingsId;

    /// <summary>
    /// The currently active workspace name. Defaults to "personal", which
    /// maps to null PartitionKey in the object store for backward compatibility.
    /// </summary>
    public string ActiveWorkspace { get; init; } = WorkspaceKeys.Personal;

    /// <summary>
    /// All workspace names that have ever been activated on this installation.
    /// "personal" is always present. New names are appended when UseWorkspace
    /// is called with a name not already in this list.
    /// </summary>
    public IReadOnlyList<string> KnownWorkspaces { get; init; } = [WorkspaceKeys.Personal];

    /// <summary>
    /// Explicit whitelist of action names allowed for autonomous execution.
    /// Default is empty for safety.
    /// </summary>
    public IReadOnlySet<string> AllowedAutomationActions { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
}
