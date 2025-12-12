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

    // public InterpreterResult Interpret (string input)
    // {
    //     // Not used anymore — retained for interface compatibility
    //     return  InterpretWithContext(input
    //                               , null!);
    // }

    public async Task<InterpreterResult> InterpretWithContext (string              input
                                                  , ConversationContext context
            )
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
        var prompt         = await BuildPromptAsync(input.Trim());
                               //, actionsSummary);
        if (context.ClarificationModeEnabled)
        {
            prompt += "\n\nCLARIFICATION_MODE = true\n";
        }

        Console.WriteLine($"actionsSummary: {actionsSummary}");
        Console.WriteLine($"prompt: {prompt}");
        
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

        Console.WriteLine($"rawResponse: {rawResponse}");
        
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
                     , DebugInfo           = debug //parsed.DebugInfo
                     , Reason              = parsed.Reason
                     , FailureType         = parsed.FailureType
                     , CandidateActions    = parsed.CandidateActions
                     , MissingParameters   = parsed.MissingParameters
               };
    }

    // ---------------------------------------------------------------------
    // Prompt builder (strong JSON compliance)
    // ---------------------------------------------------------------------
    private async Task<string> BuildPromptAsync(string userInput)
    {
        // Load system.txt
        var systemPrompt = await File.ReadAllTextAsync("Prompts/system.txt");

        // Build the dynamic actions summary (your existing method)
        var actionsSummary = BuildActionsSummary(_registry.Actions);

        // Replace placeholders
        systemPrompt = systemPrompt.Replace("{{ACTIONS}}",    actionsSummary)
                                   .Replace("{{USER_INPUT}}", userInput);

        return systemPrompt;
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
                    sb.AppendLine($"    - {p.Name} (required={!p.IsOptional}, allowEmpty={p.AllowEmpty}): \"{p.Description}\"");
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

    public async Task<string?> ReadPromptAsync(string fileName)
    {
        var filePath = Path.Combine(AppContext.BaseDirectory
                                  , "Prompts"
                                  , fileName);

        if ( ! File.Exists(filePath))
        {
            throw new FileNotFoundException($"System prompt file not found at: {filePath}");
        }

        return await File.ReadAllTextAsync(filePath);
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

                        // Validate action exists and filter missing parameters
            var actionsList = actions.ToList();

            // ----------------------------------------------------
            // Phase 3.9: Filter out OPTIONAL parameters from missingParameters
            // ----------------------------------------------------
            if (actionName is not null && missingParameters.Count > 0)
            {
                var meta = actionsList.FirstOrDefault(a =>
                    a.Name.Equals(actionName, StringComparison.OrdinalIgnoreCase));

                if (meta is not null)
                {
                    // Only keep required parameters in missingParameters
                    var requiredNames = meta.Parameters
                                            .Where(p => !p.IsOptional)
                                            .Select(p => p.Name)
                                            .ToHashSet(StringComparer.OrdinalIgnoreCase);

                    missingParameters = missingParameters
                        .Where(name => name is not null && requiredNames.Contains(name))
                        .ToList();
                }

                // If we removed all missing parameters but failureType is still MissingParameters,
                // clear the failure (no required parameters are missing anymore).
                if (missingParameters.Count == 0
                 && failureType == InterpreterFailureType.MissingParameters)
                {
                    failureType = InterpreterFailureType.None;
                }
            }

            var debug = string.Empty;

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


