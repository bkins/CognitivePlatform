namespace CognitivePlatform.Api.Workspace;

/// <summary>
/// Well-known workspace names and the mapping to IObjectStore partition keys.
///
/// <para>
/// "personal" is the default workspace and maps to <c>null</c> in the object store,
/// preserving full backward compatibility with all pre-EPIC-10 data whose
/// PartitionKey column is NULL. No migration is required for existing installations.
/// </para>
///
/// <para>
/// All other workspace names are stored as-is (lowercased) in PartitionKey.
/// </para>
/// </summary>
public static class WorkspaceKeys
{
    /// <summary>
    /// The default workspace name. Maps to <c>null</c> in the object store for
    /// backward compatibility with pre-EPIC-10 data.
    /// </summary>
    public const string Personal = "personal";

    /// <summary>
    /// Translates a logical workspace name to the IObjectStore <c>partitionKey</c>
    /// parameter. "personal" maps to <c>null</c>; all other names pass through as-is.
    /// </summary>
    public static string? ToPartitionKey(string workspaceName) =>
        workspaceName.Equals(Personal, StringComparison.OrdinalIgnoreCase)
            ? null
            : workspaceName;
}
