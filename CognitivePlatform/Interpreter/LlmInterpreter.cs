using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CognitivePlatform.Api.Conversation;
using CognitivePlatform.Api.Models;
using CognitivePlatform.Api.Registry;
using CognitivePlatform.Api.Telemetry;

namespace CognitivePlatform.Api.Interpreter;

public class LlmInterpreter : IInterpreter
{
    private readonly IActionRegistry _registry;
    private readonly ITelemetrySink  _telemetry;
    private readonly ILlmClient      _llmClient;

    public LlmInterpreter (IActionRegistry registry
                         , ITelemetrySink  telemetry
                         , ILlmClient      llmClient)
    {
        _registry  = registry;
        _telemetry = telemetry;
        _llmClient = llmClient;
    }

    public InterpreterResult Interpret (string input)
    {
        // Not used anymore — retained for interface compatibility
        return InterpretWithContext(input
                                  , null!);
    }

    public InterpreterResult InterpretWithContext (string              input
                                                 , ConversationContext context)
    {
        _telemetry.Track("Interpreter.Start"
                       , $"Input='{input}'");

        if (string.IsNullOrWhiteSpace(input))
        {
            return new InterpreterResult
                   {
                           ActionName          = null
                         , ExtractedParameters = new()
                         , DebugInfo           = "Empty input."
                         , Reason              = "Input was empty."
                         , FailureType         = InterpreterFailureType.NoMatchingAction
                   };

        }

        var actionsSummary = BuildActionsSummary(_registry.Actions);
        var prompt = BuildPrompt(input.Trim()
                               , actionsSummary);

        string rawResponse;
        try
        {
            rawResponse = _llmClient.SendAsync(prompt).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            return new InterpreterResult
                   {
                           ActionName          = null
                         , ExtractedParameters = new()
                         , DebugInfo           = $"LLM call failed: {ex.GetType().Name} - {ex.Message}"
                         , Reason              = $"LLM call failed: {ex.GetType().Name} - {ex.Message}"
                         , FailureType         = InterpreterFailureType.NoMatchingAction
                         , CandidateActions    = null
                         , MissingParameters   = null
                   };
        }

        var parsed = ParseModelResponse(rawResponse
                                      , _registry.Actions);

        var debug = new StringBuilder().AppendLine("LlmInterpreter completed.")
                                       .AppendLine($"UserInput: {input}")
                                       .AppendLine($"ModelActionName: {parsed.ActionName ?? "<null>"}")
                                       .AppendLine($"ParseDebug: {parsed.DebugInfo}")
                                       .ToString();

        _telemetry.Track("Interpreter.End"
                       , debug);

        return new InterpreterResult
               {
                       ActionName          = parsed.ActionName
                     , ExtractedParameters = parsed.Parameters
                     , DebugInfo           = parsed.DebugInfo
                     , Reason              = parsed.Reason
                     , FailureType         = parsed.FailureType
                     , CandidateActions    = parsed.CandidateActions
                     , MissingParameters   = parsed.MissingParameters
               };
    }

    // ---------------------------------------------------------------------
    // Prompt builder (strong JSON compliance)
    // ---------------------------------------------------------------------
    private static string BuildPrompt (string userInput
                                     , string actionsSummary)
    {
        //TODO: Move this to an external text/josn file, and have this method read from it
        // instead of build the text.
        var sb = new StringBuilder();

        sb.AppendLine("SYSTEM:");
        sb.AppendLine("You are the Natural Language Command Interpreter for the CognitivePlatform.");
        sb.AppendLine("Your job is to choose which action to call and extract the correct parameters.");
        sb.AppendLine("You NEVER execute actions — you only select them.");

        sb.AppendLine();
        sb.AppendLine("OUTPUT RULES:");
        sb.AppendLine("You MUST ALWAYS reply with ONLY valid JSON. No commentary, no text before or after.");
        sb.AppendLine("Your required output JSON schema is:");
        sb.AppendLine("{");
        sb.AppendLine("  \"actionName\": \"NameOfActionOrNone\",");
        sb.AppendLine("  \"parameters\": { \"paramName\": \"value\" },");
        sb.AppendLine("  \"reason\": \"Short explanation of why you chose this action or why you could not choose any.\",");
        sb.AppendLine("  \"failureType\": \"None | NoMatchingAction | AmbiguousIntent | MissingParameters\",");
        sb.AppendLine("  \"candidateActions\": [\"Optional list of candidate action names if ambiguous\"],");
        sb.AppendLine("  \"missingParameters\": [\"Optional list of missing required parameter names\"]");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("If an action applies successfully:");
        sb.AppendLine("- Set failureType to \"None\".");
        sb.AppendLine("- actionName must be the chosen action.");
        sb.AppendLine("- parameters must include all required parameters.");
        sb.AppendLine();
        sb.AppendLine("If no action applies at all:");
        sb.AppendLine("- Set actionName to \"none\".");
        sb.AppendLine("- Set failureType to \"NoMatchingAction\".");
        sb.AppendLine("- candidateActions may be empty or omitted.");
        sb.AppendLine();
        sb.AppendLine("If multiple actions seem equally likely:");
        sb.AppendLine("- Set actionName to \"none\".");
        sb.AppendLine("- Set failureType to \"AmbiguousIntent\".");
        sb.AppendLine("- candidateActions should list the names of the plausible actions.");
        sb.AppendLine();
        sb.AppendLine("If an action matches but required parameters are missing:");
        sb.AppendLine("- Set actionName to \"none\".");
        sb.AppendLine("- Set failureType to \"MissingParameters\".");
        sb.AppendLine("- candidateActions should include the target action.");
        sb.AppendLine("- missingParameters should include the parameter names that are missing.");
        sb.AppendLine();
        sb.AppendLine("If no action applies, return at minimum:");
        sb.AppendLine("{\"actionName\":\"none\",\"parameters\":{},\"reason\":\"No suitable action\",\"failureType\":\"NoMatchingAction\"}");
        sb.AppendLine();
        sb.AppendLine("META-ACTION RULES (CRITICAL):");
        sb.AppendLine("1. If the user asks to explain, describe, define, summarize, document, or clarify ANY action,");
        sb.AppendLine("   ALWAYS select the action: DescribeAction(actionName: string).");
        sb.AppendLine("   - Never call the action being described.");
        sb.AppendLine("   - The parameter actionName must EXACTLY match the target action's Name.");
        sb.AppendLine();
        sb.AppendLine("2. If the user asks:");
        sb.AppendLine("      \"What can you do?\"");
        sb.AppendLine("      \"List your commands\"");
        sb.AppendLine("      \"Show your capabilities\"");
        sb.AppendLine("   ALWAYS choose: ListActions()");
        sb.AppendLine();
        sb.AppendLine("3. NEVER hallucinate action names or parameters.");
        sb.AppendLine("4. NEVER alter parameter names — they MUST match exactly.");
        sb.AppendLine("5. Categories guide interpretation. The same action name appearing in the text DOES NOT mean");
        sb.AppendLine("   the user wants to execute that action.");
        sb.AppendLine("   Example: \"Describe the action StoreValue\" MUST NOT call StoreValue.");
        sb.AppendLine();
        sb.AppendLine("6. If unclear which action to call → choose none.");

        sb.AppendLine();
        sb.AppendLine("AVAILABLE ACTIONS:");
        sb.AppendLine(actionsSummary);

        sb.AppendLine();
        sb.AppendLine("USER:");
        sb.AppendLine(userInput);

        sb.AppendLine();
        sb.AppendLine("SYSTEM: Return ONLY the JSON. Nothing else.");

        return sb.ToString();
    }


    private static string BuildActionsSummary (IEnumerable<ActionMetadata> actions)
    {
        var sb = new StringBuilder();

        foreach (var action in actions)
        {
            sb.AppendLine($"Action: {action.Name}");
            sb.AppendLine($"  Description: {action.Description}");

            if (action.Parameters.Count > 0)
            {
                sb.AppendLine("  Parameters:");
                foreach (var p in action.Parameters)
                {
                    sb.AppendLine($"    - {p.Name}: {p.Description}");
                }
            }

            if (action.Examples is { Length: > 0 })
            {
                sb.AppendLine("  Examples:");
                foreach (var ex in action.Examples)
                    sb.AppendLine($"    - {ex}");
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    // ---------------------------------------------------------------------
    // Parsing with multi-stage JSON extraction
    // ---------------------------------------------------------------------
    private static ParsedModelResponse ParseModelResponse (string                      raw
                                                         , IEnumerable<ActionMetadata> actions)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new ParsedModelResponse
                   {
                       ActionName = null
                     , Parameters = new()
                     , DebugInfo = "Empty LLM response."
                   };
        }

        // Stage 1: direct JSON attempt
        if (TryParse(raw
                   , out var parsed1
                   , actions))
        {
            return parsed1;
        }

        // Stage 2: attempt substring extraction
        var jsonFromBraces = ExtractJsonBlock(raw);
        if (TryParse(jsonFromBraces
                   , out var parsed2
                   , actions))
        {
            return parsed2;
        }

        // Stage 3: attempt regex fallback
        var regexMatch = Regex.Match(raw
                                   , "{.*}"
                                   , RegexOptions.Singleline);
        if (regexMatch.Success
         && TryParse(regexMatch.Value
                   , out var parsed3
                   , actions))
        {
            return parsed3;
        }

        // Total failure
        return new ParsedModelResponse
               {
                       ActionName        = null
                     , Parameters        = new()
                     , DebugInfo         = $"Failed to parse model JSON. Raw response: {raw}"
                     , Reason            = "Failed to parse model JSON."
                     , FailureType       = InterpreterFailureType.NoMatchingAction
                     , CandidateActions  = null
                     , MissingParameters = null
               };



    }

    private static bool TryParse (string                      candidate
                                , out ParsedModelResponse     parsed
                                , IEnumerable<ActionMetadata> actions)
    {
        try
        {
            using var doc = JsonDocument.Parse(candidate);

            var root = doc.RootElement;
            var actionName = root.TryGetProperty("actionName"
                                               , out var actProp)
                ? actProp.GetString()
                : null;

            if (string.Equals(actionName
                            , "none"
                            , StringComparison.OrdinalIgnoreCase))
                actionName = null;

            var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (root.TryGetProperty("parameters"
                                  , out var paramsProp)
             && paramsProp.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in paramsProp.EnumerateObject())
                    parameters[p.Name] = p.Value.GetString() ?? p.Value.ToString();
            }

            // New: reason + failure metadata
            var reason = string.Empty;
            if (root.TryGetProperty("reason"
                                  , out var reasonProp))
                reason = reasonProp.GetString();

            var failureType = InterpreterFailureType.None;

            if (root.TryGetProperty("failureType"
                                  , out var failureProp))
            {
                var failureValue = failureProp.GetString();
                if (!string.IsNullOrWhiteSpace(failureValue)
                 && Enum.TryParse(failureValue
                                , ignoreCase: true
                                , out InterpreterFailureType parsedFt))
                {
                    failureType = parsedFt;
                }
            }

            var candidateActions = new List<string?>();
            if (root.TryGetProperty("candidateActions"
                                  , out var candProp)
             && candProp.ValueKind == JsonValueKind.Array)
            {
                candidateActions = candProp.EnumerateArray()
                                           .Select(item => item.GetString())
                                           .Where(name => !string.IsNullOrWhiteSpace(name))
                                           .ToList();
            }

            var missingParameters = new List<string?>();
            if (root.TryGetProperty("missingParameters"
                                  , out var missProp)
             && missProp.ValueKind == JsonValueKind.Array)
            {
                missingParameters = missProp.EnumerateArray()
                                            .Select(item => item.GetString())
                                            .Where(name => !string.IsNullOrWhiteSpace(name))
                                            .ToList();
            }

            // Validate action exists
            var debug       = string.Empty;
            var actionsList = actions.ToList();

            if (actionName != null
             && !actionsList.Any(action => action.Name.Equals(actionName
                                                            , StringComparison.OrdinalIgnoreCase)))
            {
                debug      = $"Action '{actionName}' does not exist in registry.";
                actionName = null;
                failureType = failureType == InterpreterFailureType.None
                                          ? InterpreterFailureType.NoMatchingAction
                                          : failureType;
            }
            else
            {
                debug = $"Parsed action '{actionName ?? "<null>"}' with {parameters.Count} parameter(s).";
            }

            // If model didn't specify a failure type but actionName is null,
            // assume NoMatchingAction as a sensible default.
            if (actionName is null
             && failureType == InterpreterFailureType.None)
            {
                failureType = InterpreterFailureType.NoMatchingAction;
            }

            parsed = new ParsedModelResponse
                     {
                         ActionName        = actionName
                       , Parameters        = parameters
                       , DebugInfo         = debug
                       , Reason            = reason
                       , FailureType       = failureType
                       , CandidateActions  = candidateActions
                       , MissingParameters = missingParameters
                     };
            return true;
        }
        catch
        {
            parsed = null!;
            return false;
        }
    }

    private static string ExtractJsonBlock (string text)
    {
        var first = text.IndexOf('{');
        var last  = text.LastIndexOf('}');

        return (first >= 0 && last > first) ? text[first..(last + 1)] : text;
    }
}


