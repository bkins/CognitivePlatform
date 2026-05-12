using CognitivePlatform.Api.Attributes;

namespace CognitivePlatform.Api.Workspace;

/// <summary>
/// User-facing commands for switching, querying, and listing workspaces.
///
/// <para>
/// Workspaces are lightweight data-partition scopes. Switching is instant
/// and persistent — subsequent domain commands (tasks, journal, activities)
/// read and write only data belonging to the active workspace.
/// "personal" is the default workspace and always exists.
/// </para>
/// </summary>
public class WorkspaceActions
{
    private readonly IWorkspaceContext _workspaceContext;

    public WorkspaceActions(IWorkspaceContext workspaceContext)
    {
        _workspaceContext = workspaceContext
                        ?? throw new ArgumentNullException(nameof(workspaceContext));
    }

    [FastPath]
    [NaturalLanguageAction(
        Description = "Switch to a named workspace. Creates the workspace automatically if it doesn't exist yet. "
                    + "All subsequent task, journal, and activity commands will read and write data scoped to this workspace."
      , Examples =
        [
                "Switch to my work workspace"
              , "Use the family workspace"
              , "Switch to personal"
              , "Switch workspace to health"
              , "Go to my work context"
        ]
      , IsReplayable = false)]
    public async Task<string> UseWorkspace(
        [NaturalLanguageParam(Description = "The workspace name to switch to (e.g. \"work\", \"personal\", \"family\")."
                            , Optional    = false
                            , AllowEmpty  = false)]
        string name)
    {
        var previous = _workspaceContext.ActiveWorkspace;
        await _workspaceContext.SetActiveWorkspaceAsync(name);
        var current = _workspaceContext.ActiveWorkspace;

        return previous.Equals(current, StringComparison.OrdinalIgnoreCase)
                   ? $"Already in workspace: {current}"
                   : $"Switched to workspace: {current}";
    }

    [FastPath]
    [NaturalLanguageAction(
        Description = "Get the name of the currently active workspace."
      , Examples =
        [
                "What workspace am I in?"
              , "Which workspace is active?"
              , "Show current workspace"
              , "What's my current context?"
        ])]
    public string GetCurrentWorkspace()
    {
        return $"Active workspace: {_workspaceContext.ActiveWorkspace}";
    }

    [FastPath]
    [NaturalLanguageAction(
        Description = "List all workspaces that have been used on this installation."
      , Examples =
        [
                "List my workspaces"
              , "What workspaces do I have?"
              , "Show all workspaces"
              , "Which workspaces exist?"
        ])]
    public string ListWorkspaces()
    {
        var active     = _workspaceContext.ActiveWorkspace;
        var workspaces = _workspaceContext.KnownWorkspaces;

        var items = workspaces.Select(workspace =>
                                   workspace.Equals(active, StringComparison.OrdinalIgnoreCase)
                                       ? $"{workspace} (active)"
                                       : workspace);

        return $"Workspaces: {string.Join(", ", items)}";
    }
}
