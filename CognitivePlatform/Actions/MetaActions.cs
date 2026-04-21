using System.Text;
using CognitivePlatform.Api.Attributes;
using CognitivePlatform.Api.Conversation;
using CognitivePlatform.Api.Models;
using CognitivePlatform.Api.Registry;
using CP.Shared.Primitives.Avails.Extensions;

namespace CognitivePlatform.Api.Actions;

public static class MetaActions
{
    private static IActionRegistry?     _registry;
    private static ConversationContext? _context;

    /// <summary>
    /// Called once per app lifetime (or startup) to inject the shared action registry.
    /// </summary>
    public static void SetRegistry (IActionRegistry registry)
    {
        _registry = registry;
    }

    /// <summary>
    /// Called once per request to give meta-actions access
    /// to the live conversation context.
    /// </summary>
    public static void SetContext (ConversationContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Natural-language facing action: returns a human-readable summary
    /// of all actions currently registered in the system.
    ///
    /// This is the "meta-action" the interpreter can choose when the user asks
    /// something like "What can you do?" or "List your commands."
    /// </summary>
    [NaturalLanguageAction(Description = "Lists all registered actions known to the system, including their names and descriptions."
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
    public static ListActionsResult BuildListActionsResult (IActionRegistry registry)
    {
        // Organize by category (alphabetical), then by action name
        var grouped = registry.Actions
                              .GroupBy(action => action.Category)
                              .OrderBy(grouping => grouping.Key
                                     , StringComparer.OrdinalIgnoreCase)
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

                if (action.Description.HasValue())
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

        return new ListActionsResult(
                Metadata: flatSorted
              , Summary: sb.ToString()
        );
    }

    [NaturalLanguageAction(Description = "Describes an action by name, including its category, parameters, and example phrases."
                         , Examples =
                           [
                                   "Describe the action StoreValue"
                                 , "What does RepeatLastAction do?"
                                 , "Explain the action RecallValue"
                           ]
                         , Category = "interpreter"
    )]
    public static string DescribeAction (string actionName)
    {
        if (_registry is null)
            return "The action registry is not available. MetaActions.SetRegistry(...) was not called.";

        if (actionName.HasNoValue())
            return "Please specify the name of the action you want described.";

        var action = _registry.FindByName(actionName);

        if (action is null)
            return $"I can't find any action named '{actionName}'.";

        var sb = new StringBuilder();

        sb.AppendLine($"Action: {action.Name}");
        sb.AppendLine($"Category: {action.Category}");

        if (action.Description.HasValue()) sb.AppendLine($"Description: {action.Description}");

        // Parameters
        if (action.Parameters.Count == 0)
        {
            sb.AppendLine("Parameters: (none)");
        }
        else
        {
            sb.AppendLine("Parameters:");
            foreach (var parameters in action.Parameters)
            {
                sb.Append(" - ");
                sb.Append(parameters.Name);
                sb.Append(" (");
                sb.Append(parameters.ParameterType.Name);
                sb.Append(")");

                if (parameters.Description.HasValue())
                {
                    sb.Append(": ");
                    sb.Append(parameters.Description);
                }

                sb.AppendLine();
            }
        }

        // Examples
        if (action.Examples is not { Length: > 0 }) return sb.ToString();

        sb.AppendLine("Examples:");
        foreach (var example in action.Examples)
            sb.AppendLine($" - \"{example}\"");

        return sb.ToString();
    }

    [NaturalLanguageAction(Description = "Explains why the interpreter chose (or failed to choose) the last action."
                         , Examples =
                           [
                                   "Why did you choose that?"
                                 , "Why that action?"
                                 , "Why didn't you understand me?"
                           ]
                         , Category = "interpreter"
    )]
    public static string WhyAction()
    {
        if (_context is null)
            return "No conversation context available.";

        var result = BuildWhyActionResult(_context);

        var sb = new StringBuilder();

        if (result.FailureType == InterpreterFailureType.None)
        {
            sb.AppendLine($"Last action: {result.ActionName ?? "<none>"}");
            sb.AppendLine();
            sb.AppendLine("Why it was chosen:");
            sb.AppendLine(result.Reason ?? "No interpreter reasoning was captured for the last decision.");
        }
        else
        {
            sb.AppendLine("The interpreter was not able to successfully choose or execute an action.");
            sb.AppendLine();
            sb.AppendLine($"Failure type: {result.FailureType}");
            sb.AppendLine();
            sb.AppendLine("Details:");
            sb.AppendLine(result.Reason ?? "No interpreter reasoning was captured for the last decision.");

            if (result.CandidateActions is { Count: > 0 })
            {
                sb.AppendLine();
                sb.AppendLine("Candidate actions:");
                
                foreach (var name in result.CandidateActions)
                    sb.AppendLine($" - {name}");
            }

            if (result.MissingParameters is { Count: > 0 })
            {
                sb.AppendLine();
                sb.AppendLine("Missing parameters:");
                foreach (var parameter in result.MissingParameters)
                    sb.AppendLine($" - {parameter}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("Debug snapshot:");
        sb.AppendLine(result.Debug);

        return sb.ToString();
    }

    [NaturalLanguageAction(Description = "Handles non-command conversational input."
                         , Category = "General")]
    public static string ChitChat( string text )
    {
#if DEBUG
        return $"You said: '{text}'. This is a placeholder response from the ChitChat action. "
             + $"In a real implementation, this could be a call to a language model to generate a conversational reply."
             + $"\n\n(Also, since this is a debug build, you can see the input text echoed back here for testing purposes.)";
#endif
        return "I'm here when you want to do something.";
    }

    public static WhyActionResult BuildWhyActionResult (ConversationContext ctx)
    {
        var debug = ctx.LastInterpreterDebug
                 ?? "No interpreter debug information was recorded.";

        IReadOnlyList<string>? candidates = ctx.LastCandidateActions.Count > 0
                                                    ? ctx.LastCandidateActions.AsReadOnly()
                                                    : null;

        IReadOnlyList<string>? missing = ctx.LastMissingParameters.Count > 0
                                                 ? ctx.LastMissingParameters.AsReadOnly()
                                                 : null;

        return new WhyActionResult(ActionName: ctx.LastActionName
                                 , Reason: ctx.LastInterpreterReason
                                 , Debug: debug
                                 , FailureType: ctx.LastFailureType
                                 , CandidateActions: candidates
                                 , MissingParameters: missing
        );
    }
}
