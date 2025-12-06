using System.Text;
using CognitivePlatform.Api.Attributes;
using CognitivePlatform.Api.Models;
using CognitivePlatform.Api.Registry;

namespace CognitivePlatform.Api.Actions;

public static class MetaActions
{
    private static IActionRegistry? _registry;

    /// <summary>
    /// Called once per app lifetime (or startup) to inject the shared action registry.
    /// </summary>
    public static void SetRegistry(IActionRegistry registry)
    {
        _registry = registry;
    }

    /// <summary>
    /// Natural-language facing action: returns a human-readable summary
    /// of all actions currently registered in the system.
    ///
    /// This is the "meta-action" the interpreter can choose when the user asks
    /// something like "What can you do?" or "List your commands."
    /// </summary>
    [NaturalLanguageAction(
        Description = "Lists all registered actions known to the system, including their names and descriptions.",
        Examples =
        [
              "What can you do?"
            , "List all your actions."
            , "Show me your available commands."
        ]
    )]
    public static string ListActions()
    {
        if (_registry is null)
            return "The action registry is not available yet. MetaActions.SetRegistry(...) was not called.";

        var result = BuildListActionsResult(_registry);

        // For now, we return only the human-readable summary to the user.
        // The structured Metadata is available via BuildListActionsResult for
        // other meta-features, logging, and future UI.
        return result.Summary;
    }

    /// <summary>
    /// Core introspection logic: builds a structured ListActionsResult containing
    /// both the raw metadata and a human-readable summary.
    ///
    /// This is the foundation for future meta-behavior (grouping by category, modules, etc.).
    /// </summary>
    public static ListActionsResult BuildListActionsResult(IActionRegistry registry)
    {
        var sorted = registry.Actions
                             .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                             .ToArray();

        var sb = new StringBuilder();
        sb.AppendLine($"The system currently supports {sorted.Length} actions:");

        foreach (var action in sorted)
        {
            sb.Append(" - ");
            sb.Append(action.Name);

            if (!string.IsNullOrWhiteSpace(action.Description))
            {
                sb.Append(" — ");
                sb.Append(action.Description);
            }

            sb.AppendLine();
        }

        // Future Phase 3 extension point:
        // - Group actions by category or module
        // - e.g. registry.Actions.GroupBy(a => a.Category ?? "General")

        return new ListActionsResult
        (
              Metadata: sorted
            , Summary : sb.ToString()
        );
    }
}
