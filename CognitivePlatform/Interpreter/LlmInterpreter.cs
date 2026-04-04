using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CognitivePlatform.Api.Avails;
using CognitivePlatform.Api.Conversation;
using CognitivePlatform.Api.Models;
using CognitivePlatform.Api.Registry;
using CognitivePlatform.Api.Telemetry;
using CognitivePlatform.Api.Telemetry.Events;
using CP.Shared.Primitives.Avails.Extensions;

namespace CognitivePlatform.Api.Interpreter;

public class LlmInterpreter : IInterpreter
{
    private readonly IActionRegistry   _registry;
    private readonly ITelemetrySink    _telemetry;
    private readonly ILlmClient        _llmClient;
    private readonly LlmModelCatalog   _modelCatalog;
    private readonly LlmClientSettings _settings;

    public LlmInterpreter( IActionRegistry   registry
                         , ITelemetrySink    telemetry
                         , ILlmClient        llmClient
                         , LlmModelCatalog   modelCatalog
                         , LlmClientSettings settings )
    {
        _registry     = registry;
        _telemetry    = telemetry;
        _llmClient    = llmClient;
        _modelCatalog = modelCatalog;
        _settings     = settings;
    }

    public async Task<InterpreterResult> InterpretWithContext( string              input
                                                             , ConversationContext context )
    {
        _telemetry.Track(new LlmInterpreterStartedEvent
                         {
                                 Input = input
                               , Model = _settings.Model
                         });

        if (input.HasNoValue())
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

        if (context.ClarificationModeEnabled)
            prompt += "\n\nCLARIFICATION_MODE = true\n";

        var rawResponse = string.Empty;
        var model       = string.Empty;

        try
        {
            context.Metadata.TryGetValue("model", out var requestedModel);

            // Resolve which model to actually use:
            //   1. Use the requested model if it exists in the catalog and is usable.
            //   2. Otherwise fall back to the settings default.
            //   3. If the default is also missing from the catalog, use any usable model.
            // This handles the Groq provider case where the catalog only contains the
            // probed Groq model but the client may send an Ollama model name.
            model = ResolveModel(requestedModel);

            var modelInfo = _modelCatalog.AvailableModels
                                         .FirstOrDefault(info => info.Name.Equals(model
                                                                                , StringComparison.OrdinalIgnoreCase));

            if (modelInfo is null || modelInfo.IsUsable.Not())
            {
                return new InterpreterResult
                       {
                               ActionName          = null
                             , ExtractedParameters = new()
                             , DebugInfo           = $"No usable model found. Requested: '{requestedModel}', Resolved: '{model}'."
                             , CandidateActions    = null
                             , MissingParameters   = null
                             , FailureType         = InterpreterFailureType.NoMatchingAction
                             , Reason              = $"Model '{model}' is not usable on this system."
                       };
            }

            _telemetry.Track($"LlmClient.Send; RequestedModel: {requestedModel}, ResolvedModel: {model}");

            // Pass the resolved model name to the client so each provider
            // receives its own model identifier (e.g. "llama-3.3-70b-versatile"
            // for Groq rather than the Ollama name the LAA sent).
            rawResponse = await _llmClient.SendAsync(prompt
                                                   , model
                                                   , CancellationToken.None);
        }
        catch (Exception ex)
        {
            var message = $"LLM call failed (using Model: {model}): {ex.GetType().Name} - {ex.Message}";
            _telemetry.Track($"LlmClient.Send; {message}");

            return new InterpreterResult
                   {
                           ActionName          = null
                         , ExtractedParameters = new()
                         , DebugInfo           = message
                         , Reason              = ex.GetType().Name
                         , FailureType         = InterpreterFailureType.Exception
                         , CandidateActions    = null
                         , MissingParameters   = null
                   };
        }

        Console.WriteLine($"rawResponse: {rawResponse}");

        var parsed = ParseModelResponse(rawResponse, _registry.Actions);

        var debug = new StringBuilder().AppendLine("LlmInterpreter completed.")
                                       .AppendLine($"UserInput: {input}")
                                       .AppendLine($"ModelActionName: {parsed.ActionName ?? "<null>"}")
                                       .AppendLine($"ParseDebug: {parsed.DebugInfo}")
                                       .ToString();

        _telemetry.Track($"Interpreter.End: {debug}");

        return new InterpreterResult
               {
                       ActionName          = parsed.ActionName
                     , ExtractedParameters = parsed.Parameters
                     , DebugInfo           = debug
                     , Reason              = parsed.Reason
                     , FailureType         = parsed.FailureType
                     , CandidateActions    = parsed.CandidateActions
                     , MissingParameters   = parsed.MissingParameters
               };
    }

    // ---------------------------------------------------------------------
    // Model resolution
    // ---------------------------------------------------------------------

    /// <summary>
    /// Resolves the model name to send to the LLM client.
    ///
    /// Priority:
    ///   1. The requested model, if it is in the catalog and usable.
    ///   2. The settings DefaultModel, if it is in the catalog and usable.
    ///   3. The first usable model in the catalog.
    ///   4. The settings DefaultModel as a last resort (client decides what to do).
    ///
    /// This allows the Groq provider to work correctly even when the LAA
    /// sends an Ollama model name — the catalog only contains the Groq model,
    /// so the fallback path picks it up automatically.
    /// </summary>
    private string ResolveModel(string? requestedModel)
    {
        var usable = _modelCatalog.AvailableModels
                                  .Where(info => info.IsUsable)
                                  .ToList();

        // 1. Requested model is usable
        if (requestedModel.HasValue())
        {
            var match = usable.FirstOrDefault(info => info.Name.Equals(requestedModel
                                                                      , StringComparison.OrdinalIgnoreCase));
            if (match is not null)
                return match.Name;
        }

        // 2. Settings default is usable
        if (_settings.DefaultModel.HasValue())
        {
            var defaultMatch = usable.FirstOrDefault(info => info.Name.Equals(_settings.DefaultModel
                                                                             , StringComparison.OrdinalIgnoreCase));
            if (defaultMatch is not null)
                return defaultMatch.Name;
        }

        // 3. First usable model in catalog
        if (usable.Count > 0)
            return usable[0].Name;

        // 4. Last resort — return the settings default and let the client handle it
        return _settings.DefaultModel;
    }

    // ---------------------------------------------------------------------
    // Prompt builder
    // ---------------------------------------------------------------------
    private async Task<string> BuildPromptAsync(string userInput)
    {
        var systemPrompt   = await File.ReadAllTextAsync("Prompts/system.txt");
        var actionsSummary = BuildActionsSummary(_registry.Actions);

        systemPrompt = systemPrompt.Replace("{{ACTIONS}}",    actionsSummary)
                                   .Replace("{{USER_INPUT}}", userInput);

        return systemPrompt;
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
                foreach (var parameter in action.Parameters)
                    sb.AppendLine($"    - {parameter.Name} (required={parameter.IsOptional.Not()}, allowEmpty={parameter.AllowEmpty}): \"{parameter.Description}\"");
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
        var filePath = Path.Combine(AppContext.BaseDirectory, "Prompts", fileName);

        if (File.Exists(filePath).Not())
            throw new FileNotFoundException($"System prompt file not found at: {filePath}");

        return await File.ReadAllTextAsync(filePath);
    }

    // ---------------------------------------------------------------------
    // Parsing with multi-stage JSON extraction
    // ---------------------------------------------------------------------
    private static ParsedModelResponse ParseModelResponse( string                      raw
                                                         , IEnumerable<ActionMetadata> actions )
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new ParsedModelResponse
                   {
                           ActionName = null
                         , Parameters = new()
                         , DebugInfo  = "Empty LLM response."
                   };
        }

        if (TryParse(raw, out var parsed1, actions))
            return parsed1;

        var jsonFromBraces = ExtractJsonBlock(raw);
        if (TryParse(jsonFromBraces, out var parsed2, actions))
            return parsed2;

        var regexMatch = Regex.Match(raw, "{.*}", RegexOptions.Singleline);
        if (regexMatch.Success && TryParse(regexMatch.Value, out var parsed3, actions))
            return parsed3;

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

    private static bool TryParse( string                      candidate
                                 , out ParsedModelResponse     parsed
                                 , IEnumerable<ActionMetadata> actions )
    {
        try
        {
            using var doc = JsonDocument.Parse(candidate);

            var root       = doc.RootElement;
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
                    parameters[p.Name] = NormalizeParameterValue(p.Value);
            }

            var reason = string.Empty;
            if (root.TryGetProperty("reason", out var reasonProp))
                reason = reasonProp.GetString();

            var failureType = InterpreterFailureType.None;
            if (root.TryGetProperty("failureType", out var failureProp))
            {
                var failureValue = failureProp.GetString();
                if (failureValue.HasValue()
                 && Enum.TryParse(failureValue, ignoreCase: true, out InterpreterFailureType parsedFt))
                {
                    failureType = parsedFt;
                }
            }

            var candidateActions = new List<string?>();
            if (root.TryGetProperty("candidateActions", out var candProp)
             && candProp.ValueKind == JsonValueKind.Array)
            {
                candidateActions = candProp.EnumerateArray()
                                           .Select(item => item.GetString())
                                           .Where(name => name.HasValue())
                                           .ToList();
            }

            var missingParameters = new List<string?>();
            if (root.TryGetProperty("missingParameters", out var missProp)
             && missProp.ValueKind == JsonValueKind.Array)
            {
                missingParameters = missProp.EnumerateArray()
                                            .Select(item => item.GetString())
                                            .Where(name => name.HasValue())
                                            .ToList();
            }

            var actionsList = actions.ToList();

            // Phase 3.9: Filter out optional parameters from missingParameters
            if (actionName is not null && missingParameters.Count > 0)
            {
                var meta = actionsList.FirstOrDefault(action =>
                    action.Name.Equals(actionName, StringComparison.OrdinalIgnoreCase));

                if (meta is not null)
                {
                    var requiredNames = meta.Parameters
                                            .Where(parameter => parameter.IsOptional.Not())
                                            .Select(parameter => parameter.Name)
                                            .ToHashSet(StringComparer.OrdinalIgnoreCase);

                    missingParameters = missingParameters
                                       .Where(name => name is not null && requiredNames.Contains(name))
                                       .ToList();
                }

                if (missingParameters.Count == 0
                 && failureType == InterpreterFailureType.MissingParameters)
                {
                    failureType = InterpreterFailureType.None;
                }
            }

            var debug = string.Empty;

            if (actionName != null
             && actionsList.Any(action => action.Name.IsEqualTo(actionName)).Not())
            {
                debug       = $"Action '{actionName}' does not exist in registry.";
                actionName  = null;
                failureType = failureType == InterpreterFailureType.None
                                      ? InterpreterFailureType.NoMatchingAction
                                      : failureType;
            }
            else
            {
                debug = $"Parsed action '{actionName ?? "<null>"}' with {parameters.Count} parameter(s).";
            }

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

    /// <summary>
    /// Normalizes a JSON parameter value to a string the execution engine can
    /// consume. Local LLMs sometimes emit native JSON booleans (true/false) or
    /// numbers instead of the string form the system prompt requests. We handle
    /// all value kinds explicitly so downstream bool/enum parsers always receive
    /// a predictable lowercase string.
    /// </summary>
    private static string NormalizeParameterValue(JsonElement element)
    {
        return element.ValueKind switch
        {
                JsonValueKind.String => element.GetString() ?? string.Empty
              , JsonValueKind.True   => "true"
              , JsonValueKind.False  => "false"
              , JsonValueKind.Number => element.GetRawText()
              , _                    => element.ToString()
        };
    }

    private static string ExtractJsonBlock(string text)
    {
        var first = text.IndexOf('{');
        var last  = text.LastIndexOf('}');

        return (first >= 0 && last > first)
                       ? text[first..(last + 1)]
                       : text;
    }
}