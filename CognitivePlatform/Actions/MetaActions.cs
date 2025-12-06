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
    [NaturalLanguageAction( Description = "Lists all registered actions known to the system, including their names and descriptions."
                          , Examples =
                           [
                               "What can you do?"
                             , "List all your actions."
                             , "Show me your available commands."
                           ]
                           , Category = "interpreter"
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
        // Organize by category (alphabetical), then by action name
        var grouped = registry.Actions
                              .GroupBy(a => a.Category)
                              .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                              .ToList();

        var sb = new StringBuilder();
        sb.AppendLine("The system currently supports the following actions:");

        foreach (var group in grouped)
        {
            var actions = group.OrderBy(metadata => metadata.Name
                                      , StringComparer.OrdinalIgnoreCase)
                               .ToList();

            sb.AppendLine();
            sb.AppendLine($"{group.Key} Actions ({actions.Count}):");

            foreach (var action in actions)
            {
                sb.Append(" - ");
                sb.Append(action.Name);

                if ( ! string.IsNullOrWhiteSpace(action.Description))
                {
                    sb.Append(" — ");
                    sb.Append(action.Description);
                }

                sb.AppendLine();
            }
        }

        // Structured metadata is sorted flat (alphabetical) – unchanged from Step 3.1
        var flatSorted = registry.Actions
                                 .OrderBy(metadata => metadata.Name
                                        , StringComparer.OrdinalIgnoreCase)
                                 .ToArray();

        return new ListActionsResult
        (
            Metadata: flatSorted
          , Summary : sb.ToString()
        );
    }
    
    [NaturalLanguageAction(Description = "Describes an action by name, including its category, parameters, and example phrases."
                         , Examples    =
                           [
                               "Describe the action StoreValue"
                             , "What does RepeatLastAction do?"
                             , "Explain the action RecallValue"
                           ]
                         , Category    = "interpreter"
    )]
    public static string DescribeAction(string actionName)
    {
        if (_registry is null)
            return "The action registry is not available. MetaActions.SetRegistry(...) was not called.";

        if (string.IsNullOrWhiteSpace(actionName))
            return "Please specify the name of the action you want described.";

        var action = _registry.FindByName(actionName);

        if (action is null)
            return $"I can't find any action named '{actionName}'.";

        var sb = new StringBuilder();

        sb.AppendLine($"Action: {action.Name}");
        sb.AppendLine($"Category: {action.Category}");

        if ( ! string.IsNullOrWhiteSpace(action.Description))
            sb.AppendLine($"Description: {action.Description}");

        // Parameters
        if (action.Parameters.Count == 0)
        {
            sb.AppendLine("Parameters: (none)");
        }
        else
        {
            sb.AppendLine("Parameters:");
            foreach (var p in action.Parameters)
            {
                sb.Append(" - ");
                sb.Append(p.Name);
                sb.Append(" (");
                sb.Append(p.ParameterType.Name);
                sb.Append(")");

                if (!string.IsNullOrWhiteSpace(p.Description))
                {
                    sb.Append(": ");
                    sb.Append(p.Description);
                }

                sb.AppendLine();
            }
        }

        // Examples
        if (action.Examples is not { Length: > 0 }) return sb.ToString();
        
        sb.AppendLine("Examples:");
        foreach (var ex in action.Examples)
            sb.AppendLine($" - \"{ex}\"");

        return sb.ToString();
    }

}
