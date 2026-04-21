using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using CognitivePlatform.Api.Avails;
using CognitivePlatform.Api.Contracts;
using CognitivePlatform.Api.Conversation;
using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Domains.Journal.Interfaces;
using CognitivePlatform.Api.Execution;
using CognitivePlatform.Api.Insights;
using CognitivePlatform.Api.Insights.Models;
using CognitivePlatform.Api.Interpreter;
using CognitivePlatform.Api.Models;
using CognitivePlatform.Api.Registry;
using CognitivePlatform.Api.Telemetry;
using CognitivePlatform.Api.Telemetry.Events;
using CP.Shared.Primitives.Avails.Extensions;

namespace CognitivePlatform.Api.Orchestrator;

public class ConversationOrchestrator : IConversationOrchestrator
{
    private readonly IActionRegistry          _registry;
    private readonly IInterpreter             _interpreter;   // Keyed: LlmInterpreter
    private readonly IExecutionEngine         _execution;
    private readonly ConversationContextStore _contextStore;
    private readonly ITelemetrySink           _telemetry;
    private readonly IFastPathResolver        _fastPath;
    private readonly ILlmClient               _llmClient;
    private readonly IIdempotencyStore        _idempotencyStore;
    private readonly TelemetryContext         _telemetryContext;
    private readonly IInsightEngine           _insightEngine;
    private readonly IInsightHistoryStore     _insightHistory;

    internal bool _isDebug  = false;

    public ConversationOrchestrator( IActionRegistry                                                registry
                                   , [FromKeyedServices(KeyedServices.LlmInterpreter)] IInterpreter interpreter
                                   , IExecutionEngine                                               execution
                                   , ConversationContextStore                                       contextStore
                                   , ITelemetrySink                                                 telemetry
                                   , IFastPathResolver                                              fastPathResolver
                                   , ILlmClient                                                     llmClient
                                   , IIdempotencyStore                                              idempotencyStore
                                   , TelemetryContext                                               telemetryContext
                                   , IInsightEngine                                                 insightEngine
                                   , IInsightHistoryStore                                           insightHistory )
    {
        _registry         = registry         ?? throw new ArgumentNullException(nameof(registry));
        _interpreter      = interpreter      ?? throw new ArgumentNullException(nameof(interpreter));
        _execution        = execution        ?? throw new ArgumentNullException(nameof(execution));
        _contextStore     = contextStore     ?? throw new ArgumentNullException(nameof(contextStore));
        _telemetry        = telemetry        ?? throw new ArgumentNullException(nameof(telemetry));
        _fastPath         = fastPathResolver ?? throw new ArgumentNullException(nameof(fastPathResolver));
        _llmClient        = llmClient        ?? throw new ArgumentNullException(nameof(llmClient));
        _idempotencyStore = idempotencyStore ?? throw new ArgumentNullException(nameof(idempotencyStore));
        _telemetryContext = telemetryContext  ?? throw new ArgumentNullException(nameof(telemetryContext));
        _insightEngine    = insightEngine    ?? throw new ArgumentNullException(nameof(insightEngine));
        _insightHistory   = insightHistory   ?? throw new ArgumentNullException(nameof(insightHistory));

#if DEBUG
        _isDebug = true;
#endif
    }


    public async Task<ConverseResponse> ConverseAsync(ConverseRequest    request
                                                    , CancellationToken ct = default)
    {
        var sw = new Stopwatch();
        sw.Start();

        var sessionId = request.SessionId;
        
        _telemetryContext.SessionId = sessionId;
        _telemetry.Track(_telemetryContext.CreateEvent( new ConversationStartedEvent
                         {
                                 SessionId = sessionId
                               , Sequence  = _telemetryContext.NextSequence()
                               , Input     = request.Input ?? "No input provided."
                         }));

        if (request?.Input is null) throw new ArgumentNullException(nameof(request));
        
        if (request.ClientRequestId.HasValue)
        {
            var existing = await _idempotencyStore.TryGetAsync(request.ClientRequestId.Value, ct);

            if (existing is not null)
            {
                _telemetry.Track(_telemetryContext.CreateEvent(new IdempotencyHitEvent
                                                       {
                                                               ClientRequestId = request.ClientRequestId
                                                                                        .Value
                                                                                        .ToString()
                                                       }));

                return existing;
            }
        }

        _telemetry.Track(_telemetryContext.CreateEvent(new OrchestratorStartedEvent
                                                       {
                                                               Model = request.Model ?? "No Model defined"
                                                       }));
        
        // 1. Get or create the session context
        var context = _contextStore.GetOrCreate(request.SessionId);
        
// 🔑 Wire meta-actions FIRST
        Actions.MetaActions.SetRegistry(_registry);
        Actions.MetaActions.SetContext(context);

// 🔑 Also wire test actions if they might be fast-pathed
        Actions.TestActions.SetContext(context);
        
        if (_fastPath.TryResolve(request.Input, out var actionMeta, out var fastParams)
            && actionMeta!.IsDestructive == false)
        {
            var response = TakeTheFastPath(actionMeta
                                         , fastParams
                                         , context);

            return await FinalizeAsync(request, response, sw, ct);
        }

        // Persist client-selected model (if provided) into session metadata
        if (request.Model.HasValue())
        {
            context.Metadata["model"] = request.Model?.Trim() ?? string.Empty;
        }
        else
        {
            // Prevent a prior request model from leaking into this turn
            context.Metadata.Remove("model");
        }

        // 3. If we are in a clarification flow, consume this turn
        if (context.PendingAction is not null)
        {
            var pending = context.PendingAction;

            if (pending.ConfirmationRequired)
            {
                var input = request.Input?.Trim().ToLowerInvariant();

                if (IsAffirmative(input))
                {
                    context.PendingAction = null;

                    var confirmedAction = _registry.Actions
                                          .First(action => string.Equals(action.Name
                                                                       , pending.ActionName
                                                                       , StringComparison.OrdinalIgnoreCase));

                    var execParams = ApplyDefaultValues(confirmedAction, pending.CollectedParameters);
                    var result     = _execution.Execute(confirmedAction, execParams, context.SessionId);

                    var confirmationResponse = new ConverseResponse
                                               {
                                                       Message = result
                                                     , Debug   = "Delete confirmed and executed."
                                                     , ExecutionResult = $"Executed confirmed action '{confirmedAction.Name}' "
                                                                       + $"with parameters: {string.Join(", ", execParams.Select(pair => $"{pair.Key}={pair.Value}"))}"
                                                     , WasFastPath = true
                                               };
                    return await FinalizeAsync(request, confirmationResponse, sw, ct);
                }

                if (IsNegative(input).Not())
                    return new ConverseResponse
                           {
                                   Message         = "Please confirm or cancel."
                                 , Debug           = $"Awaiting confirmation for '{pending.ActionName}'."
                                 , ExecutionResult = $"User has not yet confirmed or cancelled '{pending.ActionName}'."
                                 , WasFastPath     = true
                           };

                context.PendingAction = null;

                var response = new ConverseResponse
                               {
                                       Message         = "Cancelled."
                                     , Debug           = $"'{pending.ActionName}' cancelled by user."
                                     , ExecutionResult = $"User cancelled '{pending.ActionName}' during confirmation step."
                                     , WasFastPath     = true
                               };
                return await FinalizeAsync(request
                                         , response
                                         , sw
                                         , ct);

            }
            
            // Look up action metadata
            var action = _registry.Actions
                                  .FirstOrDefault(action => string.Equals(action.Name
                                                                        , pending.ActionName
                                                                        , StringComparison.OrdinalIgnoreCase));

            if (action is null)
            {
                // Safety: clear the pending action so we don't get stuck
                context.PendingAction = null;

                
                const string message = "The action I was trying to clarify is no longer available.";
                
                _telemetry.Track(_telemetryContext.CreateEvent(new OrchestratorProgressEvent
                                                       {
                                                               Details = $"{message} (Action: {pending.ActionName})"
                                                       }));
                var response = new ConverseResponse
                               {
                                       Message         = message
                                     , Debug           = "Pending action not found in registry."
                                     , ExecutionResult = $"Could not find action '{pending.ActionName}' in registry during clarification flow."
                                     , WasFastPath     = true
                               };
                return await FinalizeAsync(request, response, sw, ct);
            }

            // If somehow no remaining parameters, just execute with what we have
            if (pending.RemainingParameters.Count == 0)
            {
                context.PendingAction = null;

                var finalParameters = ApplyDefaultValues(action, pending.CollectedParameters);
                var execOutput      = _execution.Execute(action, finalParameters, context.SessionId);

                _telemetry.Track(_telemetryContext.CreateEvent(new OrchestratorProgressEvent
                                                       {
                                                               Details = $"Clarification.Completed; Action={pending.ActionName}, Collected={pending.CollectedParameters.Count}"
                                                       }));

                var response = new ConverseResponse
                               {
                                       Message = execOutput
                                     , Debug   = $"Executed pending action '{pending.ActionName}' with previously collected parameters."
                                     , ExecutionResult = $"Executed action '{action.Name}' "
                                                       + $"with parameters: {string.Join(", ", finalParameters.Select(pair => $"{pair.Key}={pair.Value}"))}"
                                     , WasFastPath = true
                               };
                return await FinalizeAsync(request, response, sw, ct);
            }

            // Take the next missing parameter name
            var nextParameterName = pending.RemainingParameters[0];

            // Whatever the user typed on this turn becomes the value for that parameter.
            var userValue = request.Input ?? string.Empty;

            pending.CollectedParameters[nextParameterName] = userValue;
            pending.RemainingParameters.RemoveAt(0);

            _telemetry.Track(_telemetryContext.CreateEvent(new OrchestratorProgressEvent
                                                   {
                                                           Details = $"Clarification.ParameterCollected; Action={pending.ActionName}, Parameter={nextParameterName}"
                                                   }));

            // If there are still parameters to collect, ask for the next one
            if (pending.RemainingParameters.Count > 0)
            {
                var followingName = pending.RemainingParameters[0];
                var paramMeta     = action.Parameters.FirstOrDefault(parameters => string.Equals(parameters.Name
                                                                                               , followingName
                                                                                               , StringComparison.OrdinalIgnoreCase));

                var friendlyName = string.IsNullOrWhiteSpace(paramMeta?.Description)
                                           ? followingName
                                           : paramMeta.Description;

                var question = pending.CollectedParameters.Count == 1
                                       ? $"Got it. Now I need a value for '{friendlyName}'. What should it be?"
                                       : $"Still need a value for '{friendlyName}'. What should it be?";

                context.PendingAction = pending;

                var response = new ConverseResponse
                               {
                                       Message = question
                                     , Debug = $"Clarification: collected '{nextParameterName}' = '{userValue}'. "
                                             + $"Still need parameter '{friendlyName}'."
                                     , ExecutionResult = $"Collected parameter '{nextParameterName}' with value '{userValue}' for action '{action.Name}'. Still need parameter '{friendlyName}'."
                                     , WasFastPath = true
                               };
                
                return await FinalizeAsync(request, response, sw, ct);
            }

            // execute the action now
            context.PendingAction = null;

            var parameters  = ApplyDefaultValues(action, pending.CollectedParameters);
            var finalOutput = _execution.Execute(action, parameters, context.SessionId);

            _telemetry.Track(_telemetryContext.CreateEvent(new OrchestratorProgressEvent
                                                   {
                                                           Details = $"Clarification.Completed; Action={pending.ActionName}, Collected={pending.CollectedParameters.Count}"
                                                   }));

            var finalClarificationResponse = new ConverseResponse
                                             {
                                                     Message = finalOutput
                                                   , Debug = $"Executed pending action '{pending.ActionName}' "
                                                           + $"after collecting all required parameters."
                                                   , ExecutionResult = $"Executed action '{action.Name}' with parameters: {string.Join(", ", parameters.Select(pair => $"{pair.Key}={pair.Value}"))}"
                                                   , WasFastPath = true
                                             };
            return await FinalizeAsync(request, finalClarificationResponse, sw, ct);
        }

        // 4. Log interpreter identity
        _telemetry.Track(_telemetryContext.CreateEvent(new OrchestratorProgressEvent
                                                       {
                                                               Details = $"Interpreter.Selected; Using interpreter: {_interpreter.GetType().Name}"
                                                       }));
        // TODO: Define `WasResolvedFor` first
        // Debug.Assert(!_fastPath.WasResolvedFor(request),
        //              "Interpreter should not run after FastPath resolution.");
        var interpretation = await _interpreter.InterpretWithContext(request.Input, context);
       
        
        // 5a. By default, clear any pending action; clarification will set it again
        context.PendingAction = null;

        // 5b. Persist interpreter decision (success or failure) into context
        context.LastUserMessage          = request.Input;
        context.LastActionName           = interpretation.ActionName;
        context.LastInterpreterReason    = interpretation.Reason;
        context.LastInterpreterDebug     = interpretation.DebugInfo;
        context.LastFailureType          = interpretation.FailureType;
        context.LastInterpreterException = interpretation.Exception;
        
        if (interpretation.FailureType == InterpreterFailureType.Exception)
        {
            var message = "Something went wrong while processing your request. Please try again.";
            if (_isDebug)
            {
                message = $"""
                          ## Something went wrong while processing your request.
                          ----
                          You are getting this because:
                          ```csharp
                          interpretation.FailureType == InterpreterFailureType.Exception
                          {interpretation.Exception?.ToString() ?? "No exception details available."}
                          ```
                          Is `true`
                          The exception is:
                          >{interpretation.DebugInfo}
                          """;
            }

            var llmExceptionResponse = new ConverseResponse
                                       {
                                               Message         = message
                                             , Debug           = interpretation.DebugInfo
                                             , ExecutionResult = $"Interpreter reason: {interpretation.Reason}"
                                             , Success         = false
                                       };
            return await FinalizeAsync(request, llmExceptionResponse, sw, ct);
        }
        
        
        context.LastCandidateActions.Clear();
        
        if (interpretation.CandidateActions is { Count: > 0 })
        {
            foreach (var name in interpretation.CandidateActions)
                context.LastCandidateActions.Add(name);
        }

        context.LastMissingParameters.Clear();
        if (interpretation.MissingParameters is { Count: > 0 })
        {
            foreach (var missingItem in interpretation.MissingParameters)
                context.LastMissingParameters.Add(missingItem);
        }

        context.LastParameters.Clear();
        
        foreach (var pair in interpretation.ExtractedParameters)
            context.LastParameters[pair.Key] = pair.Value;

        // 6. Handle the "missing required parameters" failure case
        if (interpretation is
            {
                    FailureType: InterpreterFailureType.MissingParameters
                  , ActionName: not null
                  , MissingParameters.Count: > 0
            })
        {
            // Look up action metadata
            var action = _registry.Actions.FirstOrDefault(metadata => string.Equals(metadata.Name
                                                                                  , interpretation.ActionName
                                                                                  , StringComparison.OrdinalIgnoreCase));

            if (action is null)
            {
                var msg = $"Interpreter selected unknown action '{interpretation.ActionName}'.";
                
                _telemetry.Track(_telemetryContext.CreateEvent(new OrchestratorProgressEvent
                                                       {
                                                               Details = $"ActionLookup.Failed; {msg}"
                                                       }));

                var response = new ConverseResponse
                       {
                               Message = "That action is not registered in this system."
                             , Debug   = msg
                             , ExecutionResult = $"Could not find action '{interpretation.ActionName}' in registry during missing parameters handling."
                       };
                return await  FinalizeAsync(request, response, sw, ct);
            }

            if (action.AllowsClarification)
            {
                // Normalize missing parameter names
                var missingNames = interpretation.MissingParameters
                                                 .Where(value => value.HasValue())
                                                 .Select(value => value.Trim())
                                                 .Distinct(StringComparer.OrdinalIgnoreCase)
                                                 .ToList();

                // Special handling for StoreValue: always ask for "key" first, then "value"
                if (action.Name.Equals("StoreValue", StringComparison.OrdinalIgnoreCase))
                {
                    var ordered = new List<string>();

                    if (missingNames.Any(name => name.Equals("key", StringComparison.OrdinalIgnoreCase)))
                        ordered.Add("key");

                    if (missingNames.Any(name => name.Equals("value", StringComparison.OrdinalIgnoreCase)))
                        ordered.Add("value");

                    // Fall back to whatever we got if something unexpected happens
                    if (ordered.Count > 0)
                        missingNames = ordered;
                }

                var firstMissing = missingNames[0];

                var paramMeta = action.Parameters
                                      .FirstOrDefault(metadata => string.Equals(metadata.Name
                                                                              , firstMissing
                                                                              , StringComparison.OrdinalIgnoreCase));

                var friendlyName = string.IsNullOrWhiteSpace(paramMeta?.Description)
                                           ? firstMissing
                                           : paramMeta.Description;

                string question;

                if (action.Name.Equals("StoreValue"
                                     , StringComparison.OrdinalIgnoreCase)
                 && firstMissing.Equals("key"
                                      , StringComparison.OrdinalIgnoreCase))
                {
                    // Very explicit wording so the user knows the next utterance becomes the literal key
                    question = "I can store that value, but I need the literal key to store it under. "
                             + "Please tell me exactly what key to use next – whatever you type will be used verbatim as the key.";
                }
                else
                {
                    question = $"I can run {action.Name}, but I need a value for '{friendlyName}'. What should it be?";
                }

                context.PendingAction = new PendingAction
                                        {
                                                ActionName          = action.Name
                                              , CollectedParameters = new Dictionary<string, string>(interpretation.ExtractedParameters
                                                                                                   , StringComparer.OrdinalIgnoreCase)
                                              , RemainingParameters = missingNames
                                        };

                var response = new ConverseResponse
                               {
                                       Message = question
                                     , Debug   = interpretation.DebugInfo
                                     , ExecutionResult = $"Interpreter selected action '{action.Name}' but is missing required parameters: {string.Join(", ", missingNames)}. Prompting user for '{friendlyName}'."
                               };
                return await  FinalizeAsync(request, response, sw, ct);
            }

            // Action does NOT allow clarification: treat as a normal failure
            var missingJoined = string.Join(", ", interpretation.MissingParameters);

            var message = "I understood what you want to do, but I'm missing some required details. Could you rephrase with more specifics?";
            if (_isDebug)
            {
                message = """
                          ## Missing required parameters — action does not allow clarification.
                          ----
                          You are getting this because:
                          ```csharp
                          if (interpretation is
                          {
                                  FailureType: InterpreterFailureType.MissingParameters
                                , ActionName: not null
                                , MissingParameters.Count: > 0
                          })
                          ```
                          Is `true` 
                          """;
            }

            var missingParametersResponse = new ConverseResponse
                                            {
                                                    Message         = message
                                                  , Debug           = $"Missing required parameters for action '{interpretation.ActionName}': {missingJoined}"
                                                  , ExecutionResult = $"Interpreter reason: {interpretation.Reason}"
                                            };
            return await FinalizeAsync(request, missingParametersResponse, sw, ct);
        }
        
        // 7. No action chosen at all (e.g. nonsense input or other failure)
        if (interpretation.ActionName.HasNoValue())
        {
            var message = "I didn't recognize that as something I can do. Try 'what can you do' to see available commands.";
            if (_isDebug)
            {
                message = """
                          ## No action recognized.
                          ----
                          You are getting this because:
                          ```csharp
                          if (string.IsNullOrWhiteSpace(interpretation.ActionName))
                          ```
                          Is `true`

                          """;
            }
            var missingActionResponse = new ConverseResponse
                   {
                           Message = message
                         , Debug   = interpretation.DebugInfo
                   };
            return await FinalizeAsync(request, missingActionResponse, sw, ct);
        }

        // 8. Look up the action reflectively
        var selectedAction = _registry.Actions.FirstOrDefault(metadata => string.Equals(metadata.Name
                                                                                      , interpretation.ActionName
                                                                                      , StringComparison.OrdinalIgnoreCase));

        if (selectedAction is null)
        {
            var msg = $"Interpreter selected unknown action '{interpretation.ActionName}'.";
            _telemetry.Track(_telemetryContext.CreateEvent(new OrchestratorProgressEvent
                                                   {
                                                           Details = $"ActionLookup.Failed; {msg}"
                                                   }));

            var unknownActionResponse = new ConverseResponse
                                        {
                                                Message = "That action is not registered in this system."
                                              , Debug   = msg
                                        };
            return await FinalizeAsync(request, unknownActionResponse, sw, ct);

        }

        // Generic confirmation gate: any action decorated with [DestructiveAction]
        // requires explicit user confirmation before execution.
        if (selectedAction.IsDestructive)
        {
            var confirmationMessage = BuildDestructiveConfirmationPrompt(selectedAction
                                                                       , interpretation.ExtractedParameters);

            context.PendingAction = new PendingAction
                                    {
                                            ActionName           = selectedAction.Name
                                          , CollectedParameters  = new Dictionary<string, string>(interpretation.ExtractedParameters
                                                                                                , StringComparer.OrdinalIgnoreCase)
                                          , RemainingParameters  = new List<string>()
                                          , ConfirmationRequired = true
                                          , ConfirmationPrompt   = confirmationMessage
                                    };

            var response = new ConverseResponse
                           {
                                   Message         = confirmationMessage
                                 , Debug           = $"Destructive action '{selectedAction.Name}' requires confirmation."
                                 , ExecutionResult = $"Awaiting user confirmation before executing '{selectedAction.Name}'."
                           };
            return await FinalizeAsync(request, response, sw, ct);
        }
        
        // 9. Execute (sync) with whatever parameters we have (including defaults for optionals)
        var execParameters  = ApplyDefaultValues(selectedAction, interpretation.ExtractedParameters);
        var execOutputFinal = _execution.Execute(selectedAction, execParameters, context.SessionId);

        // Insight Engine — runs after execution; only pays LLM cost when insights exist
        var insights = await _insightEngine.GenerateInsightsAsync(context, ct);
        string finalMessage;

        if (insights.Count > 0)
        {
            await _insightHistory.RecordEmittedAsync(insights, ct);

            context.LastEmittedInsights = insights
                .Select(insight => new EmittedInsightRef(insight.DeduplicationKey
                                                        , insight.SuggestedAction))
                .ToList();

            finalMessage = await WeaveInsightsAsync(execOutputFinal, insights, ct);
        }
        else
        {
            context.LastEmittedInsights = [];
            finalMessage = execOutputFinal;
        }

        // 10. Return a consolidated response after finalizing it
        var finalResponse = new ConverseResponse
                            {
                                    Message = finalMessage
                                  , Debug   = interpretation.DebugInfo
                                  , ExecutionResult = $"Executed action '{selectedAction.Name}' with parameters: {string.Join(", ", execParameters.Select(pair => $"{pair.Key}={pair.Value}"))}"
                            };

        return await FinalizeAsync(request, finalResponse, sw, ct);
    }

    private ConverseResponse TakeTheFastPath( ActionMetadata?             actionMeta
                                            , Dictionary<string, string>? fastParams
                                            , ConversationContext         context )
    {
        _telemetry.Track(_telemetryContext.CreateEvent(new OrchestratorProgressEvent
                                                       {
                                                               Details = $"FastPath.Resolved; Action={actionMeta!.Name}"
                                                       }));

        if (actionMeta!.IsDestructive)
        {
            var confirmationMessage = BuildDestructiveConfirmationPrompt(actionMeta, fastParams!);

            context.PendingAction = new PendingAction
                                    {
                                            ActionName           = actionMeta.Name
                                          , CollectedParameters  = new Dictionary<string, string>(fastParams!
                                                                                                , StringComparer.OrdinalIgnoreCase)
                                          , RemainingParameters  = new List<string>()
                                          , ConfirmationRequired = true
                                          , ConfirmationPrompt   = confirmationMessage
                                    };

            return new ConverseResponse
                   {
                           Message         = confirmationMessage
                         , Debug           = $"FastPath destructive action '{actionMeta.Name}' requires confirmation."
                         , ExecutionResult = $"Awaiting user confirmation before executing '{actionMeta.Name}'."
                         , WasFastPath     = true
                   };
        }

        var result = _execution.Execute(actionMeta!, fastParams!, context.SessionId);

        var parameters = string.Join(", ", fastParams!.Select(pair => $"{pair.Key}: {pair.Value}"));

        return new ConverseResponse
               {
                       Message         = result
                     , Debug           = $"FastPath → Action={actionMeta!.Name} with Params=[{parameters}]"
                     , ExecutionResult = $"Successfully executed FastPath-resolved action '{actionMeta.Name}'\n"
                                       + $"                  with parameters: {parameters}"
                     , WasFastPath     = true
               };
    }

    private static string BuildDestructiveConfirmationPrompt( ActionMetadata               action
                                                             , IDictionary<string, string> parameters )
    {
        var paramSummary = parameters.Count > 0
                                   ? string.Join(", ", parameters.Select(p => $"{p.Key}: {p.Value}"))
                                   : "(no parameters)";

        return $"You are about to run '{action.Name}'. This action cannot be undone.\n\n"
             + $"Parameters: {paramSummary}\n\n"
             + "Please confirm or cancel.";
    }

    private bool IsNegative (string? input)
    {
        return input is "no" or "cancel" or "never mind";
    }

    private bool IsAffirmative (string? input)
    {
        return input is "yes" or "confirm" or "delete" or "do it";
    }

    public async IAsyncEnumerable<string> StreamAsync(ConverseRequest                            request,
                                                      [EnumeratorCancellation] CancellationToken ct = default)
    {
        var sw = new Stopwatch();
        sw.Start();
        
        var sessionId = request.SessionId;
        _telemetryContext.SessionId = sessionId;
        _telemetry.Track(_telemetryContext.CreateEvent(new ConversationStartedEvent
                                                       {
                                                               Input       = request.Input ?? "No input provided."
                                                             , IsStreaming = true
                                                       }));

        if (request?.Input is null)
            yield break;

        // Get or create session context
        var context = _contextStore.GetOrCreate(request.SessionId);

        // Persist model choice if supplied
        if (request.Model.HasValue())
            context.Metadata["model"] = request.Model.Trim();
        else
            context.Metadata.Remove("model");

        // Wire context (same as ConverseAsync)
        Actions.TestActions.SetContext(context);
        Actions.MetaActions.SetRegistry(_registry);
        Actions.MetaActions.SetContext(context);

        // ✅ J-01.1: FastPath always wins (for non-destructive actions).
        // In streaming mode: if FastPath resolves a non-destructive action, execute
        // and emit a single chunk. Destructive actions fall through to the interpreter.
        if (_fastPath.TryResolve(request.Input, out var actionMeta, out var fastParams)
            && actionMeta!.IsDestructive == false)
        {
            _telemetry.Track(_telemetryContext.CreateEvent(new OrchestratorProgressEvent
                                                   {
                                                           Details = $"FastPath.Resolved.Stream; Action={actionMeta!.Name}"
                                                   }));

            // Mirror ConverseAsync J-03 behavior for journal fast-path:
            // if (actionMeta.Name.Equals("AddJournalEntry", StringComparison.OrdinalIgnoreCase))
            // {
            //     var parsed = _journalParser.Parse(request.Input);
            //
            //     var draft = new JournalDraft
            //                 {
            //                         Text = parsed.Text
            //                       , Tags = parsed.Tags
            //                       , Mood = parsed.Mood
            //                       , State = JournalDraftState.Local
            //                 };
            //
            //     await _journalDraftRepository.AddAsync(draft, ct);
            // }
            
            var result = _execution.Execute(actionMeta!, fastParams!, context.SessionId);
            _telemetry.Track(_telemetryContext.CreateEvent(new OrchestratorProgressEvent
                                                   {
                                                           Details = $"FastPath.Executed.Stream; Action={actionMeta.Name}; Result {result}\n"
                                                   }));

            yield return result;
            yield break;
        }

        if (context.PendingAction is not null)
        {
            yield return "Clarification flows do not support streaming.";
            yield break;
        }
        
//slow here
        var interpretation = await _interpreter.InterpretWithContext(request.Input, context);

        // 🚫 If an action was selected, DO NOT stream
        if (interpretation.ActionName.HasValue())
        {
            var result = await ConverseAsync(request, ct);
            
            yield return result.Message ?? string.Empty;
            yield break;
        }

        // 🔥 Stream directly from the LLM
        await foreach (var chunk in _llmClient.StreamAsync(request.Input
                                                         , request.Model
                                                         , ct))
        {
            yield return chunk;
        }
        
        //TODO:  Figure out how to determine when the stream is complete.
        // Does this need to be determined in the controller?  
    }

    private async Task<string> WeaveInsightsAsync( string                 execOutput
                                                  , IReadOnlyList<Insight> insights
                                                  , CancellationToken      ct )
    {
        var insightMessages = string.Join("\n", insights.Select(insight => $"- {insight.Message}"));

        var prompt = new StringBuilder();
        prompt.AppendLine("You are a helpful assistant. Present the result below to the user first, then naturally");
        prompt.AppendLine("transition into the suggestions as conversational follow-on sentences — not as a bullet");
        prompt.AppendLine("list. The suggestions should feel like a thoughtful aside, not a notification.");
        prompt.AppendLine();
        prompt.AppendLine($"Result: {execOutput}");
        prompt.AppendLine();
        prompt.AppendLine("Suggestions to weave in:");
        prompt.AppendLine(insightMessages);

        return await _llmClient.SendAsync(prompt.ToString(), cancellationToken: ct);
    }

    private static IDictionary<string, string> ApplyDefaultValues(ActionMetadata               action
                                                                 , IDictionary<string, string> parameters)
    {
        // Make a copy we can mutate
        var result = new Dictionary<string, string>(parameters, StringComparer.OrdinalIgnoreCase);

        foreach (var param in action.Parameters.Where(param => param.IsOptional))
        {
            if (param.DefaultValue is null) continue;

            var hasValue = result.TryGetValue(param.Name, out var existing)
                        && existing.HasValue();

            if (hasValue.Not())
            {
                result[param.Name] = param.DefaultValue.ToString() ?? string.Empty;
            }
        }

        return result;
    }

    public async Task<ConverseResponse> FinalizeAsync( ConverseRequest   request
                                                      , ConverseResponse  response
                                                       , Stopwatch sw
                                                      , CancellationToken ct )
    {
        if (request.ClientRequestId.HasValue)
        {
            await _idempotencyStore.StoreAsync(request.ClientRequestId.Value
                                             , response
                                             , ct);
        }

        var property = new Dictionary<string, object?>();
        property.Add("DebugInfo", response.Debug ?? "No debug info.");

        sw.Stop();

        _telemetry.Track(_telemetryContext.CreateEvent(new OrchestratorCompletedEvent
                                                       {
                                                               Model      = request.Model            ?? "No Model defined"
                                                             , Response   = response.ExecutionResult ?? "No execution result."
                                                             , Properties = property
                                                       }));
        
        _telemetry.Track(_telemetryContext.CreateEvent(new ConversationCompletedEvent
                                                       {
                                                               TimeElapsed = sw.Elapsed
                                                       }));

        // _telemetry.Track($"Orchestrator.End; Model='{request.Model}' Response: {response.ExecutionResult}");
        // _telemetry.Track("Orchestrator.End.Debug", response.Debug ?? "No debug info.");
        return response;
    }
}