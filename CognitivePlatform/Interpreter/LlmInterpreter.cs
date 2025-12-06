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
    private readonly ITelemetrySink _telemetry;
    private readonly ILlmClient _llmClient;

    public LlmInterpreter(IActionRegistry registry, ITelemetrySink telemetry, ILlmClient llmClient)
    {
        _registry = registry;
        _telemetry = telemetry;
        _llmClient = llmClient;
    }

    public InterpreterResult Interpret(string input)
    {
        // Not used anymore — retained for interface compatibility
        return InterpretWithContext(input, null!);
    }
    
    public InterpreterResult InterpretWithContext(string input, ConversationContext context)
    {
        _telemetry.Track("Interpreter.Start", $"Input='{input}'");

        if (string.IsNullOrWhiteSpace(input))
        {
            return new InterpreterResult
            {
                ActionName = null,
                ExtractedParameters = new(),
                DebugInfo = "Empty input."
            };
        }

        var actionsSummary = BuildActionsSummary(_registry.Actions);
        var prompt = BuildPrompt(input.Trim(), actionsSummary);

        string rawResponse;
        try
        {
            rawResponse = _llmClient.SendAsync(prompt).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            return new InterpreterResult
            {
                ActionName = null,
                ExtractedParameters = new(),
                DebugInfo = $"LLM call failed: {ex.GetType().Name} - {ex.Message}"
            };
        }

        var parsed = ParseModelResponse(rawResponse, _registry.Actions);

        var debug = new StringBuilder()
            .AppendLine("LlmInterpreter completed.")
            .AppendLine($"UserInput: {input}")
            .AppendLine($"ModelActionName: {parsed.ActionName ?? "<null>"}")
            .AppendLine($"ParseDebug: {parsed.DebugInfo}")
            .ToString();

        _telemetry.Track("Interpreter.End", debug);

        return new InterpreterResult
        {
            ActionName = parsed.ActionName,
            ExtractedParameters = parsed.Parameters,
            DebugInfo = parsed.DebugInfo
        };
    }

    // ---------------------------------------------------------------------
    // Prompt builder (strong JSON compliance)
    // ---------------------------------------------------------------------
    private static string BuildPrompt (string userInput
                                     , string actionsSummary)
    {
        var sb = new StringBuilder();

        sb.AppendLine("SYSTEM:");
        sb.AppendLine("You are the Natural Language Command Interpreter for the CognitivePlatform.");
        sb.AppendLine("Your job is to choose which action to call and extract the correct parameters.");
        sb.AppendLine("You NEVER execute actions — you only select them.");

        sb.AppendLine();
        sb.AppendLine("OUTPUT RULES:");
        sb.AppendLine("You MUST ALWAYS reply with ONLY valid JSON. No commentary, no text before or after.");
        sb.AppendLine("Your JSON schema is:");
        sb.AppendLine("{");
        sb.AppendLine("  \"actionName\": \"NameOfActionOrNone\",");
        sb.AppendLine("  \"parameters\": { \"paramName\": \"value\" },");
        sb.AppendLine("  \"reason\": \"Short explanation\"");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("If no action applies, return:");
        sb.AppendLine("{\"actionName\": \"none\", \"parameters\": {}, \"reason\": \"No suitable action\"}");

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


    private static string BuildActionsSummary(IEnumerable<ActionMetadata> actions)
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
    private static ParsedModelResponse ParseModelResponse(string raw, IEnumerable<ActionMetadata> actions)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new ParsedModelResponse
            {
                ActionName = null,
                Parameters = new(),
                DebugInfo = "Empty LLM response."
            };
        }

        // Stage 1: direct JSON attempt
        if (TryParse(raw, out var parsed1))
            return parsed1;

        // Stage 2: attempt substring extraction
        var jsonFromBraces = ExtractJsonBlock(raw);
        if (TryParse(jsonFromBraces, out var parsed2))
            return parsed2;

        // Stage 3: attempt regex fallback
        var regexMatch = Regex.Match(raw, "{.*}", RegexOptions.Singleline);
        if (regexMatch.Success && TryParse(regexMatch.Value, out var parsed3))
            return parsed3;

        // Total failure
        return new ParsedModelResponse
        {
            ActionName = null,
            Parameters = new(),
            DebugInfo = $"Failed to parse model JSON. Raw response: {raw}"
        };

        // Local parse method
        bool TryParse(string candidate, out ParsedModelResponse parsed)
        {
            try
            {
                using var doc = JsonDocument.Parse(candidate);
                var root = doc.RootElement;

                var actionName = root.TryGetProperty("actionName", out var actProp)
                                 ? actProp.GetString()
                                 : null;

                if (string.Equals(actionName, "none", StringComparison.OrdinalIgnoreCase))
                    actionName = null;

                var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                if (root.TryGetProperty("parameters", out var paramsProp)
                    && paramsProp.ValueKind == JsonValueKind.Object)
                {
                    foreach (var p in paramsProp.EnumerateObject())
                        parameters[p.Name] = p.Value.GetString() ?? p.Value.ToString();
                }

                // Validate action exists
                string debug;
                var actionsList = actions.ToList();

                if (actionName != null &&
                    !actionsList.Any(a => a.Name.Equals(actionName, StringComparison.OrdinalIgnoreCase)))
                {
                    debug = $"Action '{actionName}' does not exist in registry.";
                    actionName = null;
                }
                else
                {
                    debug = $"Parsed action '{actionName ?? "<null>"}' with {parameters.Count} parameter(s).";
                }

                parsed = new ParsedModelResponse
                {
                    ActionName = actionName,
                    Parameters = parameters,
                    DebugInfo = debug
                };
                return true;
            }
            catch
            {
                parsed = null!;
                return false;
            }
        }
    }

    private static string ExtractJsonBlock(string text)
    {
        var first = text.IndexOf('{');
        var last = text.LastIndexOf('}');
        return (first >= 0 && last > first) ? text[first..(last + 1)] : text;
    }
}

public class ParsedModelResponse
{
    public string? ActionName { get; init; }
    public Dictionary<string, string> Parameters { get; init; } = new();
    public string DebugInfo { get; init; } = "";
}


// using System.Text;
// using System.Text.Json;
// using CognitivePlatform.Api.Conversation;
// using CognitivePlatform.Api.Models;
// using CognitivePlatform.Api.Registry;
// using CognitivePlatform.Api.Telemetry;
//
// namespace CognitivePlatform.Api.Interpreter;
//
// /// <summary>
// /// Phase 2: Real LLM-backed interpreter.
// /// Uses the action registry to build a prompt and asks an LLM
// /// to select the best action + parameters, returning a structured
// /// <see cref="InterpreterResult"/>.
// /// </summary>
// public class LlmInterpreter : IInterpreter
// {
//     private readonly IActionRegistry _registry;
//     private readonly ITelemetrySink  _telemetry;
//     private readonly ILlmClient      _llmClient;
//
//     public LlmInterpreter(IActionRegistry registry
//                         , ITelemetrySink  telemetry
//                         , ILlmClient      llmClient)
//     {
//         _registry  = registry;
//         _telemetry = telemetry;
//         _llmClient = llmClient;
//     }
//
//     public InterpreterResult Interpret(string input)
//         => InterpretInternal(input, contextSummary: string.Empty);
//
//     public InterpreterResult InterpretWithContext(string input, ConversationContext context)
//     {
//         var contextSummary = BuildContextSummary(context);
//         return InterpretInternal(input, contextSummary);
//     }
//
//     /// <summary>
//     /// Shared core implementation that takes a raw user input and an optional
//     /// context summary string.
//     /// </summary>
//     private InterpreterResult InterpretInternal(string input, string contextSummary)
//     {
//         _telemetry.Track("Interpreter.Start", $"Input='{input}'");
//
//         var trimmed = input?.Trim() ?? string.Empty;
//
//         if (string.IsNullOrWhiteSpace(trimmed))
//         {
//             const string msg = "Input was empty or whitespace.";
//             _telemetry.Track("Interpreter.End", msg);
//
//             return new InterpreterResult
//                    {
//                        ActionName          = null,
//                        DebugInfo           = msg,
//                        ExtractedParameters = new Dictionary<string, string>()
//                    };
//         }
//
//         // Build a compact description of all available actions
//         var actionsSummary = BuildActionsSummary(_registry.Actions);
//         var prompt         = BuildPrompt(trimmed, actionsSummary, contextSummary);
//
//         string rawResponse;
//         try
//         {
//             // IInterpreter is synchronous, so we block here.
//             rawResponse = _llmClient.SendAsync(prompt)
//                                     .GetAwaiter()
//                                     .GetResult();
//         }
//         catch (Exception ex)
//         {
//             var error = $"LLM call failed: {ex.GetType().Name} - {ex.Message}";
//             _telemetry.Track("Interpreter.Error", error);
//
//             return new InterpreterResult
//                    {
//                        ActionName          = null,
//                        DebugInfo           = error,
//                        ExtractedParameters = new Dictionary<string, string>()
//                    };
//         }
//
//         var parsedModelResponse = ParseModelResponse(rawResponse, _registry.Actions);
//
//         var finalDebug = new StringBuilder()
//                          .AppendLine("LlmInterpreter completed.")
//                          .AppendLine($"UserInput: {trimmed}")
//                          .AppendLine($"ModelActionName: {parsedModelResponse.ActionName ?? "<null>"}")
//                          .AppendLine($"ParseDebug: {parsedModelResponse.DebugInfo}")
//                          .ToString();
//
//         _telemetry.Track("Interpreter.End", finalDebug);
//
//         return new InterpreterResult
//                {
//                    ActionName          = parsedModelResponse.ActionName,
//                    DebugInfo           = finalDebug,
//                    ExtractedParameters = parsedModelResponse.Parameters
//                };
//     }
//
//     private static string BuildContextSummary(ConversationContext ctx)
//     {
//         var sb = new StringBuilder();
//
//         if (ctx.LastUserMessage is not null)
//             sb.AppendLine($"LastUserMessage: {ctx.LastUserMessage}");
//
//         if (ctx.LastActionName is not null)
//             sb.AppendLine($"LastAction: {ctx.LastActionName}");
//
//         if (ctx.LastParameters.Count > 0)
//         {
//             sb.AppendLine("LastParameters:");
//             foreach (var kv in ctx.LastParameters)
//                 sb.AppendLine($"  - {kv.Key}: {kv.Value}");
//         }
//
//         return sb.ToString();
//     }
//
//     private static string BuildActionsSummary(IEnumerable<ActionMetadata> actions)
//     {
//         var sb = new StringBuilder();
//
//         foreach (var action in actions)
//         {
//             sb.AppendLine($"Action: {action.Name}");
//             sb.AppendLine($"  Description: {action.Description}");
//
//             if (action.Parameters.Count > 0)
//             {
//                 sb.AppendLine("  Parameters:");
//                 foreach (var metadata in action.Parameters)
//                 {
//                     var optional = metadata.Optional ? "optional" : "required";
//                     sb.AppendLine($"    - {metadata.Name} ({optional}): {metadata.Description}");
//                 }
//             }
//
//             if (action.Examples is { Length: > 0 })
//             {
//                 sb.AppendLine("  Examples:");
//                 foreach (var example in action.Examples)
//                 {
//                     sb.AppendLine($"    - {example}");
//                 }
//             }
//
//             sb.AppendLine();
//         }
//
//         sb.AppendLine($"InterpreterUsed: {nameof(LlmInterpreter)}");
//
//         return sb.ToString();
//     }
//
//     private static string BuildPrompt(
//         string userInput,
//         string actionsSummary,
//         string contextSummary)
//     {
//         var sb = new StringBuilder();
//
//         sb.AppendLine("You are an intent router for a C# command system.");
//         sb.AppendLine("Conversation context may help you infer missing parameters.");
//         sb.AppendLine();
//         sb.AppendLine("Conversation Context:");
//         sb.AppendLine(contextSummary);
//         sb.AppendLine();
//         sb.AppendLine("Available Actions:");
//         sb.AppendLine(actionsSummary);
//         sb.AppendLine();
//         sb.AppendLine("User Request:");
//         sb.AppendLine(userInput);
//
//         return sb.ToString();
//     }
//
//     private static ParsedModelResponse ParseModelResponse(string rawResponse, IEnumerable<ActionMetadata> actions)
//     {
//         var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
//
//         if (string.IsNullOrWhiteSpace(rawResponse))
//         {
//             return new ParsedModelResponse
//                    {
//                        ActionName = null,
//                        Parameters = parameters,
//                        DebugInfo  = "Model response was empty."
//                    };
//         }
//
//         var jsonLike = ExtractJsonBlock(rawResponse);
//
//         try
//         {
//             using var doc = JsonDocument.Parse(jsonLike);
//
//             var root = doc.RootElement;
//
//             string? actionName = null;
//
//             if (root.TryGetProperty("actionName", out var actionProp))
//             {
//                 actionName = actionProp.GetString();
//                 if (string.Equals(actionName, "none", StringComparison.OrdinalIgnoreCase))
//                 {
//                     actionName = null;
//                 }
//             }
//
//             if (root.TryGetProperty("parameters", out var paramsProp)
//              && paramsProp.ValueKind == JsonValueKind.Object)
//             {
//                 foreach (var prop in paramsProp.EnumerateObject())
//                 {
//                     var value = prop.Value.ValueKind == JsonValueKind.String
//                                     ? prop.Value.GetString()
//                                     : prop.Value.ToString();
//
//                     if (!string.IsNullOrEmpty(value))
//                     {
//                         parameters[prop.Name] = value!;
//                     }
//                 }
//             }
//
//             // Validate that the action exists in the registry.
//             var debug = string.Empty;
//
//             if (actionName is null)
//             {
//                 debug = "Parsed JSON successfully but no action selected (actionName null or 'none').";
//             }
//             else
//             {
//                 var exists = actions.Any(a => string.Equals(a.Name,
//                                                             actionName,
//                                                             StringComparison.OrdinalIgnoreCase));
//                 if (!exists)
//                 {
//                     debug      = $"Parsed action '{actionName}', but it does not exist in the registry. Treating as no-action.";
//                     actionName = null;
//                 }
//                 else
//                 {
//                     debug = $"Parsed action '{actionName}' with {parameters.Count} parameter(s).";
//                 }
//             }
//
//             return new ParsedModelResponse
//                    {
//                        ActionName = actionName,
//                        Parameters = parameters,
//                        DebugInfo  = debug
//                    };
//         }
//         catch (Exception ex)
//         {
//             var snippet = rawResponse.Length > 300
//                               ? rawResponse[..300] + "..."
//                               : rawResponse;
//
//             var debug = $"Failed to parse model JSON: {ex.GetType().Name} - {ex.Message}. " +
//                         $"Raw snippet: {snippet}";
//
//             return new ParsedModelResponse
//                    {
//                        ActionName = null,
//                        Parameters = parameters,
//                        DebugInfo  = debug
//                    };
//         }
//     }
//
//     private static string ExtractJsonBlock(string text)
//     {
//         // In case the model ignores instructions and wraps JSON in extra text,
//         // try to grab from first '{' to last '}'.
//         var first = text.IndexOf('{');
//         var last  = text.LastIndexOf('}');
//
//         if (first >= 0
//          && last > first)
//         {
//             return text.Substring(first, last - first + 1);
//         }
//
//         // Fallback: assume the whole thing is JSON-ish.
//         return text;
//     }
// }
//
// public class ParsedModelResponse
// {
//     public string?                    ActionName { get; init; }
//     public Dictionary<string, string> Parameters { get; init; }
//     public string                     DebugInfo  { get; init; }
// }
