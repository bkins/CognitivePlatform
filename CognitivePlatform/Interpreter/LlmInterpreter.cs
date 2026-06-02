using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CognitivePlatform.Api.Actions;
using CognitivePlatform.Api.Avails;
using CognitivePlatform.Api.Conversation;
using CognitivePlatform.Api.Models;
using CognitivePlatform.Api.Registry;
using CognitivePlatform.Api.Registry.Capabilities;
using CognitivePlatform.Api.Registry.Domains;
using CognitivePlatform.Api.SystemPromptLogging;
using CognitivePlatform.Api.Telemetry;
using CognitivePlatform.Api.Telemetry.Events;
using CP.Shared.Primitives.Avails.Extensions;

namespace CognitivePlatform.Api.Interpreter;

public class LlmInterpreter : IInterpreter
{
    private readonly ICapabilityRegistry _registry;
    private readonly ITelemetrySink    _telemetry;
    private readonly ILlmRouter        _llmRouter;
    private readonly LlmModelCatalog   _modelCatalog;
    private readonly LlmClientSettings _settings;
    private readonly IPromptLogger     _promptLogger;

    // Per-domain prompt summary cache. Domains are registered once at startup and never
    // change at runtime, so this cache never needs invalidation. Instance-level so
    // that test calls to the static BuildDomainAwareActionsSummary don't pollute it.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string?>
        _domainSummaryCache = new(StringComparer.OrdinalIgnoreCase);

    public LlmInterpreter( ICapabilityRegistry registry
                         , ITelemetrySink    telemetry
                         , ILlmRouter        llmRouter
                         , LlmModelCatalog   modelCatalog
                         , LlmClientSettings settings 
                         , IPromptLogger     promptLogger)
    {
        _registry     = registry;
        _telemetry    = telemetry;
        _llmRouter    = llmRouter;
        _modelCatalog = modelCatalog;
        _settings     = settings;
        _promptLogger = promptLogger;
    }

    public async Task<InterpreterResult> InterpretWithContext( string              input
                                                             , ConversationContext context
                                                             , TaskComplexity      complexity = TaskComplexity.Standard )
    {
        _telemetry.Track(new LlmInterpreterStartedEvent
                         {
                                 Input = input
                               , Model = _settings.Model
                         });

        if (input.HasNoValue())
        {
            var noInputResult = new InterpreterResult
                                {
                                        ActionName          = null
                                      , ExtractedParameters = new()
                                      , DebugInfo           = "Empty input."
                                      , Reason              = "Input was empty."
                                      , FailureType         = InterpreterFailureType.NoMatchingAction
                                };
            _telemetry.Track(noInputResult.ToEvent());
            
            return noInputResult;
        }


        var prompt = await BuildPromptAsync(input.Trim(), context);

        if (context.ClarificationModeEnabled)
            prompt += "\n\nCLARIFICATION_MODE = true\n";

        var rawResponse = string.Empty;
        var model       = string.Empty;

        try
        {
            context.Metadata.TryGetValue("model", out var requestedModel);

            // Catalog-usability pre-flight only applies when the session is using the
            // default (probed) provider. A mid-session SetProvider switch targets a
            // provider the catalog never probed, so the router handles dispatch and
            // the target provider's client is trusted to surface model errors.
            var usesDefaultProvider = UsesDefaultProvider(context);

            if (usesDefaultProvider)
            {
                // Resolve which model to actually use:
                //   1. Use the requested model if it exists in the catalog and is usable.
                //   2. Otherwise fall back to the settings default.
                //   3. If the default is also missing from the catalog, use any usable model.
                model = ResolveModel(requestedModel);

                var modelInfo = _modelCatalog.AvailableModels
                                             .FirstOrDefault(info => info.Name.Equals(model
                                                                                    , StringComparison.OrdinalIgnoreCase));

                if (modelInfo is null || modelInfo.IsUsable.Not())
                {
                    var moreInfo = modelInfo?.FailureReason ?? ".";
                    if (moreInfo != ".")
                    {
                        moreInfo = $". Reason: {GetLimitType(moreInfo)}";
                        
                    }

                    var failureReason = modelInfo?.FailureReason ?? string.Empty;
                    var extraReason   = (   failureReason.Contains("HTTP 429:")
                                         || failureReason.Contains("HTTP 503:"))
                                              ? $"\n{failureReason}"
                                              : string.Empty;
                    
                    var noModelResult= new InterpreterResult
                           {
                                   ActionName          = null
                                 , ExtractedParameters = new()
                                 , DebugInfo           = $"No usable model found. Requested: '{requestedModel}', Resolved: '{model}'."
                                 , CandidateActions    = null
                                 , MissingParameters   = null
                                 , FailureType         = InterpreterFailureType.NoMatchingAction
                                 , Reason              = $"Model '{model}' is not usable on this system{moreInfo}{extraReason}"
                           };

                    _telemetry.Track(noModelResult.ToEvent());

                    return noModelResult;
                }

                // Reflect the catalog-resolved model back into Metadata so the router
                // picks it up; the same key ("model") is where the router looks first.
                context.Metadata["model"] = model;
            }
            else
            {
                model = requestedModel ?? string.Empty;
            }

            _telemetry.Track(new LlmInterpreterProgressEvent()
                             {
                                     Details = $" : InterpretWithContext : RequestedModel: {requestedModel}, ResolvedModel: {model}"
                             });
            
            // Log ONLY system-generated prompt (this is the final payload sent to LLM)
            _promptLogger.Log(description: "IntentInterpretation"
                            , prompt:      prompt
                            , provider:    _settings.Provider.ToString()
                            , model:       model);

            // Send to model
            // Route through ILlmRouter so mid-session provider switches take effect
            // on this turn. The router re-reads context.Metadata for every call.
            var llmResult  = await _llmRouter.SendAsync(prompt
                                                         , context
                                                         , complexity: complexity
                                                         , ct:         CancellationToken.None);
            rawResponse = llmResult.Content;
        }
        catch (Exception ex)
        {
            var message = $"LLM call failed (using Model: {model}): {ex.GetType().Name} - {ex.Message}";

            _telemetry.Track(new LlmInterpreterErrorEvent
                             {
                                     Details   = message
                                   , Exception = ex
                             });

            var errorResult = new InterpreterResult
                              {
                                      ActionName          = null
                                    , ExtractedParameters = new()
                                    , DebugInfo           = message
                                    , Reason              = ex.GetType().Name
                                    , FailureType         = InterpreterFailureType.Exception
                                    , Exception           = ex
                                    , CandidateActions    = null
                                    , MissingParameters   = null
                              };
            
            _telemetry.Track(errorResult.ToEvent());
            
            return errorResult;
        }

        Console.WriteLine($"rawResponse: {rawResponse}");

        var parsed = ParseModelResponse(rawResponse, _registry.GetAll());

        var debug = new StringBuilder().AppendLine("LlmInterpreter completed.")
                                       .AppendLine($"UserInput: {input}")
                                       .AppendLine($"ModelActionName: {parsed.ActionName ?? "<null>"}")
                                       .AppendLine($"ParseDebug: {parsed.DebugInfo}")
                                       .AppendLine($"Raw Response: {rawResponse}")
                                       .ToString();

        var results = new InterpreterResult
               {
                       ActionName          = parsed.ActionName
                     , ExtractedParameters = parsed.Parameters
                     , DebugInfo           = debug + (parsed.GeminiError is not null
                                                        ? $"\nGeminiError: Code={parsed.GeminiError?.Error.Code}, Message={parsed.GeminiError?.Error.Message}"
                                                        : string.Empty)
                     , Reason              = parsed.Reason
                     , FailureType         = parsed.FailureType
                     , CandidateActions    = parsed.CandidateActions
                     , MissingParameters   = parsed.MissingParameters
               };
        
        _telemetry.Track(results.ToEvent());

        return results;
    }

    public enum LimitType
    {
        Unknown
      , RateLimit // RPM/TPM - Wait a few seconds/minutes
      , DailyQuota // RPD - Wait until next day
    }

    private (bool HasErrors, bool HasDetails, JsonElement Details) HasErrorsDetails(JsonElement root)
    {
        var hasErrors = root.TryGetProperty("error", out var error);
        var hasDetails = error.TryGetProperty("details", out var details);
        
        return (hasErrors, hasDetails, details);
    }
    
    private (bool Continue, JsonElement Violations) ContinueWithViolations(JsonElement detail)
    {
        var noViolations = detail.TryGetProperty("violations", out var violations).Not();
        var noQuotaFailure = detail.TryGetProperty("@type", out var type).Not()
                           || (type.GetString()?.Contains("QuotaFailure").Not() ?? true);
        
        return (noViolations || noQuotaFailure, violations);
    }
    
    public LimitType GetLimitType(string jsonResponse)
    {
        try 
        {
            if (jsonResponse.StartsWith("HTTP "))
            {
                jsonResponse = ExtractJsonBlock(jsonResponse);
            }
            
            using var doc  = JsonDocument.Parse(jsonResponse);

            var root = doc.RootElement;

            var response = HasErrorsDetails(root);
            
            // The error details are nested in an array
            if (response is { HasErrors: true, HasDetails: true })
            {
                foreach (var detail in response.Details.EnumerateArray())
                {
                    // Looking for QuotaFailures
                    var (shouldContinue, violations) = ContinueWithViolations(detail);

                    if (shouldContinue) continue;
                    
                    var metric = violations[0].GetProperty("quotaMetric").GetString();

                    // Logic: If it contains "day", it's the hard cap
                    return metric?.Contains("day", StringComparison.OrdinalIgnoreCase) ?? false 
                                   ? LimitType.DailyQuota 
                                   : LimitType.RateLimit;
                }
            }
        }
        catch { /*Handle malformed JSON*/ }

        return LimitType.Unknown;
    }
    
    // ---------------------------------------------------------------------
    // Session provider check
    // ---------------------------------------------------------------------

    /// <summary>
    /// True when the session has not switched away from the configured default
    /// provider. Determines whether the catalog-usability pre-flight applies.
    /// </summary>
    private bool UsesDefaultProvider(ConversationContext context)
    {
        var session = context.CurrentLlmSession;

        if (session.HasProvider.Not())
            return true;

        if (Enum.TryParse<LlmProvider>(session.Provider, ignoreCase: true, out var parsed).Not())
            return true;

        return parsed == _settings.Provider;
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
    private async Task<string> BuildPromptAsync(string userInput, ConversationContext context)
    {
        var systemPrompt   = await File.ReadAllTextAsync("Prompts/system.txt");
        var actionsSummary = BuildDomainAwareActionsSummary(
            _registry.GetAll()
          , userInput
          , domainName => _domainSummaryCache.GetOrAdd(domainName
                                                     , name => _registry.GetDomainPromptSummary(name)));
        var sessionState   = BuildSessionStateBlock(context);

        systemPrompt = systemPrompt.Replace("{{ACTIONS}}",       actionsSummary)
                                   .Replace("{{SESSION_STATE}}", sessionState)
                                   .Replace("{{USER_INPUT}}",    userInput);

        return systemPrompt;
    }

    internal static string BuildSessionStateBlock(ConversationContext context)
    {
        var today        = DateTime.Now.Date;
        var daysToMonday = ((int)today.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;

        var thisWeekStart  = today.AddDays(-daysToMonday);
        var thisWeekEnd    = thisWeekStart.AddDays(6);
        var lastWeekStart  = thisWeekStart.AddDays(-7);
        var lastWeekEnd    = thisWeekStart.AddDays(-1);
        var thisMonthStart = new DateTime(today.Year, today.Month, 1);
        var thisMonthEnd   = thisMonthStart.AddMonths(1).AddDays(-1);
        var lastMonthStart = thisMonthStart.AddMonths(-1);
        var lastMonthEnd   = thisMonthStart.AddDays(-1);

        var sb = new StringBuilder();

        sb.AppendLine("---------------------------------------------------------------------");
        sb.AppendLine("SESSION STATE (computed at request time — use these exact dates):");
        sb.AppendLine("---------------------------------------------------------------------");
        sb.AppendLine($"Today:      {today:yyyy-MM-dd} ({today:dddd})");
        sb.AppendLine($"Yesterday:  {today.AddDays(-1):yyyy-MM-dd}");
        sb.AppendLine($"Tomorrow:   {today.AddDays(1):yyyy-MM-dd}");
        sb.AppendLine($"This week:  {thisWeekStart:yyyy-MM-dd} to {thisWeekEnd:yyyy-MM-dd}  (Mon-Sun)");
        sb.AppendLine($"Last week:  {lastWeekStart:yyyy-MM-dd} to {lastWeekEnd:yyyy-MM-dd}  (Mon-Sun)");
        sb.AppendLine($"This month: {thisMonthStart:yyyy-MM-dd} to {thisMonthEnd:yyyy-MM-dd}");
        sb.AppendLine($"Last month: {lastMonthStart:yyyy-MM-dd} to {lastMonthEnd:yyyy-MM-dd}");

        if (context.LastActionName.HasValue())
            sb.AppendLine($"Last action: {context.LastActionName}");

        if (context.Metadata.TryGetValue("calendar_connected", out var calendarConnected)
            && calendarConnected.Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            sb.AppendLine("Calendar: connected");
        }

        return sb.ToString();
    }

    internal static string BuildActionsSummary(IEnumerable<ActionMetadata> actions)
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
                {
                    var typeName = GetFriendlyTypeName(parameter.ParameterType);
                    sb.AppendLine($"    - {parameter.Name} ({typeName}, required={parameter.IsOptional.Not()}, allowEmpty={parameter.AllowEmpty}): \"{parameter.Description}\"");
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
    // Domain-aware actions summary — ENH-27
    // ---------------------------------------------------------------------

    /// <summary>
    /// Scores which domains are relevant to the user's input by checking whether
    /// any of a domain's <see cref="IDomainDefinition.Keywords"/> appear in the
    /// lower-cased input string. Returns the set of matching domain names.
    ///
    /// The <c>System</c> domain is excluded from the returned set because it is
    /// always promoted to full-detail regardless of keyword scoring.
    /// An empty return set signals "no domain detected" — the caller should fall
    /// back to the full action catalog.
    /// </summary>
    internal static IReadOnlySet<string> ScoreRelevantDomains(
        string                         userInput
      , IEnumerable<IDomainDefinition> domains)
    {
        if (string.IsNullOrWhiteSpace(userInput))
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var lowerInput = userInput.ToLowerInvariant();
        var relevant   = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var domain in domains)
        {
            if (domain.Name.Equals("System", StringComparison.OrdinalIgnoreCase))
                continue; // System domain handled separately — always full

            foreach (var keyword in domain.Keywords)
            {
                if (lowerInput.Contains(keyword))
                {
                    relevant.Add(domain.Name);
                    break;
                }
            }
        }

        return relevant;
    }

    /// <summary>
    /// Builds the AVAILABLE ACTIONS block for the system prompt with domain-aware slicing.
    ///
    /// <list type="bullet">
    ///   <item>The <c>System</c> domain is always emitted in full (meta-actions such as
    ///         ChitChat, ListActions, DescribeAction are needed on every turn).</item>
    ///   <item>Domains whose keywords match <paramref name="userInput"/> are emitted in full.</item>
    ///   <item>All other domains are emitted as a compact summary line so the LLM knows
    ///         they exist without consuming extra tokens.</item>
    ///   <item>When no non-System domain matches the input, all domains are emitted in full
    ///         (safe fallback — same as the pre-ENH-27 behaviour).</item>
    /// </list>
    /// </summary>
    internal static string BuildDomainAwareActionsSummary(
        IEnumerable<ActionMetadata> actions
      , string                      userInput
      , Func<string, string?>       getDomainSummary)
    {
        var allActions = actions.ToList();

        // Collect unique domain definitions keyed by name.
        var domainsByName = allActions
            .Where(action  => action.Domain is not null)
            .GroupBy(action => action.Domain!.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key
                        , group => group.First().Domain!
                        , StringComparer.OrdinalIgnoreCase);

        var relevantDomainNames = ScoreRelevantDomains(userInput, domainsByName.Values);

        // No non-System keyword matched: fall back to the full flat catalog so we
        // never hide a relevant action from the model (same behaviour as pre-ENH-27).
        if (relevantDomainNames.Count == 0)
            return BuildActionsSummary(allActions);

        // Group actions by domain name preserving a stable order.
        var groupedByDomain = allActions
            .GroupBy(action => action.Domain?.Name ?? "General", StringComparer.OrdinalIgnoreCase)
            .ToList();

        var sb = new StringBuilder();

        foreach (var group in groupedByDomain)
        {
            var domainName  = group.Key;
            var isSystem    = domainName.Equals("System", StringComparison.OrdinalIgnoreCase);
            var isRelevant  = isSystem || relevantDomainNames.Contains(domainName);

            // Actions with no [Domain] attribute always emit in full — they can't be
            // scored by ScoreRelevantDomains, so compacting them would silently hide
            // actions such as ReportBug or StoreValue from the LLM.
            var isNullDomain = !domainsByName.ContainsKey(domainName);
            if (isRelevant || isNullDomain)
            {
                // Full per-action detail for this domain.
                foreach (var action in group)
                {
                    sb.AppendLine($"Action: {action.Name}");
                    sb.AppendLine($"  Description: {action.Description}");

                    if (action.Parameters.Count > 0)
                    {
                        sb.AppendLine("  Parameters:");
                        foreach (var parameter in action.Parameters)
                        {
                            var typeName = GetFriendlyTypeName(parameter.ParameterType);
                            sb.AppendLine($"    - {parameter.Name} ({typeName}, required={parameter.IsOptional.Not()}, allowEmpty={parameter.AllowEmpty}): \"{parameter.Description}\"");
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
            }
            else
            {
                // Compact summary for non-relevant domains.
                var summary      = getDomainSummary(domainName);
                var domainDef    = domainsByName.GetValueOrDefault(domainName);
                var summaryText  = summary ?? domainDef?.Description ?? domainName;
                var actionNames  = string.Join(", ", group.Select(action => action.Name));

                sb.AppendLine($"Domain: {domainName} — {summaryText}");
                sb.AppendLine($"  Available actions: {actionNames}");
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    internal static string GetFriendlyTypeName(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type);
        if (underlying != null)
            return GetFriendlyTypeName(underlying) + "?";

        if (type == typeof(string))         return "string";
        if (type == typeof(bool))           return "bool";
        if (type == typeof(int))            return "int";
        if (type == typeof(long))           return "long";
        if (type == typeof(double))         return "double";
        if (type == typeof(float))          return "float";
        if (type == typeof(decimal))        return "decimal";
        if (type == typeof(DateTimeOffset)) return "DateTimeOffset";
        if (type == typeof(DateTime))       return "DateTime";
        if (type == typeof(DateOnly))       return "DateOnly";

        return type.Name;
    }

    public async Task<string?> ReadPromptAsync(string fileName)
    {
        var filePath = Path.Combine(AppContext.BaseDirectory, "Prompts", fileName);

        if (File.Exists(filePath).Not())
            throw new FileNotFoundException($"System prompt file not found at: {filePath}");

        return await File.ReadAllTextAsync(filePath);
    }

    private static GeminiErrorResponse ParseGeminiError(string raw)
    {
        var emptyResponse = new GeminiErrorResponse
                            {
                                    Error = new ErrorContent
                                            {
                                                    Code    = -1
                                                  , Message = "Empty response from model."
                                            }
                            };
#if DEBUG
        emptyResponse.Error.Message += $"\nRaw response: {raw}";
#endif
        
        if (raw.HasNoValue()) return emptyResponse;


        GeminiErrorResponse? firstValue; //= result?.FirstOrDefault();

        try
        {
            var result = JsonSerializer.Deserialize<List<GeminiErrorResponse>>(raw);
            firstValue = result?.FirstOrDefault();
        }
        catch (Exception e)
        {
            emptyResponse.Error.Message = $"Failed to parse Gemini error response: {e.GetType().Name} - {e.Message}\n{e.StackTrace}";

            return emptyResponse;
        }
        
        return firstValue ?? emptyResponse;
    }
    // ---------------------------------------------------------------------
    // Parsing with multi-stage JSON extraction
    // ---------------------------------------------------------------------
    internal static ParsedModelResponse ParseModelResponse( string                      raw
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
        if (raw.Contains("HTTP 429:") 
         || raw.Contains("HTTP 503:"))
        {
            return new ParsedModelResponse
                   {
                           GeminiError = ParseGeminiError(jsonFromBraces)
                   };
        }
        
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

            // Phase 3.9: Filter out optional parameters from missingParameters.
            // When the LLM returns actionName "none" but reports a single candidate,
            // use that candidate for the lookup so that actions where every parameter
            // is optional (e.g. CloseDay) are not blocked by a spurious MissingParameters.
            var phase39LookupName = actionName
                                 ?? (candidateActions.Count == 1 ? candidateActions[0] : null);

            if (phase39LookupName is not null && missingParameters.Count > 0)
            {
                var meta = actionsList.FirstOrDefault(candidate =>
                    candidate.Name.Equals(phase39LookupName, StringComparison.OrdinalIgnoreCase));

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
                    actionName ??= phase39LookupName;
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
