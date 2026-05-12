namespace CognitivePlatform.Api.Workspace;

/// <summary>
/// Singleton implementation of <see cref="IWorkspaceContext"/>.
///
/// <para>
/// Loads the active workspace and known-workspace list from
/// <see cref="IUserSettingsService"/> at construction time and caches both
/// in memory for fast reads. On <see cref="SetActiveWorkspaceAsync"/> the
/// in-memory values are updated first (visible immediately to all callers),
/// then persisted so they survive a restart.
/// </para>
///
/// <para>
/// Thread safety: <c>_activeWorkspace</c> is <c>volatile</c>. The known-
/// workspaces list is replaced atomically via a new reference assignment
/// (volatile write). Single-writer contract matches the LlmSession pattern in
/// ConversationContext — the orchestrator processes one SetActiveWorkspace
/// command at a time; concurrent readers always observe a consistent snapshot.
/// </para>
/// </summary>
public sealed class WorkspaceContext : IWorkspaceContext
{
    private readonly IUserSettingsService _userSettings;

    private volatile string               _activeWorkspace;
    private volatile IReadOnlyList<string> _knownWorkspaces;

    public WorkspaceContext(IUserSettingsService userSettings)
    {
        _userSettings    = userSettings;
        var settings     = userSettings.Get();
        _activeWorkspace = settings.ActiveWorkspace;
        _knownWorkspaces = settings.KnownWorkspaces.Count > 0
                               ? settings.KnownWorkspaces
                               : [WorkspaceKeys.Personal];
    }

    /// <inheritdoc />
    public string ActiveWorkspace => _activeWorkspace;

    /// <inheritdoc />
    public string? ActivePartitionKey =>
        WorkspaceKeys.ToPartitionKey(_activeWorkspace);

    /// <inheritdoc />
    public IReadOnlyList<string> KnownWorkspaces => _knownWorkspaces;

    /// <inheritdoc />
    public async Task SetActiveWorkspaceAsync(string workspaceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceName);

        var normalised = workspaceName.Trim().ToLowerInvariant();

        // Update active workspace in-memory immediately.
        _activeWorkspace = normalised;

        // Append to known workspaces if this is a new name.
        if (!_knownWorkspaces.Contains(normalised, StringComparer.OrdinalIgnoreCase))
            _knownWorkspaces = [.._knownWorkspaces, normalised];

        await _userSettings.SaveAsync(new UserSettings
        {
            ActiveWorkspace  = normalised,
            KnownWorkspaces  = _knownWorkspaces
        });
    }
}
