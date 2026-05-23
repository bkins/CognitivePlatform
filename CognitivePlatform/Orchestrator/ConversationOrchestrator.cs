using System.Diagnostics;
using System.Runtime.CompilerServices;

using CP.Shared.Primitives.Avails.Extensions;

using CognitivePlatform.Api.Avails;
using CognitivePlatform.Api.Contracts;
using CognitivePlatform.Api.Conversation;
using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Domains.Activity;
using CognitivePlatform.Api.Execution;
using CognitivePlatform.Api.Insights;
using CognitivePlatform.Api.Insights.Models;
using CognitivePlatform.Api.Interpreter;
using CognitivePlatform.Api.Models;
using CognitivePlatform.Api.Registry;
using CognitivePlatform.Api.Telemetry;
using CognitivePlatform.Api.Telemetry.Events;
using CognitivePlatform.Api.Domains.PersonaEngine;
using CognitivePlatform.Api.Workspace;

namespace CognitivePlatform.Api.Orchestrator;

public class ConversationOrchestrator : IConversationOrchestrator
{
    private readonly IActionRegistry          _registry;
    private readonly IInterpreter             _interpreter;   // Keyed: LlmInterpreter
    private readonly IExecutionEngine         _execution;
    private readonly ConversationContextStore _contextStore;
    private readonly ITelemetrySink           _telemetry;
    private readonly IFastPathResolver        _fastPath;
    private readonly IWorkspaceContext        _workspaceContext;
    private readonly ILlmRouter               _llmRouter;
    private readonly IIdempotencyStore        _idempotencyStore;
    private readonly TelemetryContext         _telemetryContext;
    private readonly IInsightEngine           _insightEngine;
    private readonly IInsightHistoryStore     _insightHistory;
    private readonly IActivityLog             _activityLog;
    private readonly LlmModelCatalog          _modelCatalog;
    private readonly LlmProviderDefaults      _providerDefaults;
    private readonly ILlmRateLimiter          _rateLimiter;
    private readonly IPersonaEngine?          _personaEngine;
    private readonly IConversationTurnStore   _turnStore;
    private readonly ITaskComplexityClassifier _complexityClassifier;

    private readonly bool _isDebug  = false;

    public ConversationOrchestrator( IActionRegistry                                                registry
                                   , [FromKeyedServices(KeyedServices.LlmInterpreter)] IInterpreter interpreter
                                   , IExecutionEngine                                               execution
                                   , ConversationContextStore                                       contextStore
                                   , ITelemetrySink                                                 telemetry
                                   , IFastPathResolver                                              fastPathResolver
                                   , IWorkspaceContext                                              workspaceContext
                                   , ILlmRouter                                                     llmRouter
                                   , IIdempotencyStore                                              idempotencyStore
                                   , TelemetryContext                                               telemetryContext
                                   , IInsightEngine                                                 insightEngine
                                   , IInsightHistoryStore                                           insightHistory
                                   , IActivityLog                                                   activityLog
                                   , LlmModelCatalog                                                modelCatalog
                                   , LlmProviderDefaults                                            providerDefaults
                                   , ILlmRateLimiter                                                rateLimiter
                                   , IConversationTurnStore                                         turnStore
                                   , ITaskComplexityClassifier                                      complexityClassifier
                                   , IPersonaEngine?                                                personaEngine = null )
    {
        _registry         = registry         ?? throw new ArgumentNullException(nameof(registry));
        _interpreter      = interpreter      ?? throw new ArgumentNullException(nameof(interpreter));
        _execution        = execution        ?? throw new ArgumentNullException(nameof(execution));
        _contextStore     = contextStore     ?? throw new ArgumentNullException(nameof(contextStore));
        _telemetry        = telemetry        ?? throw new ArgumentNullException(nameof(telemetry));
        _fastPath         = fastPathResolver ?? throw new ArgumentNullException(nameof(fastPathResolver));
        _workspaceContext = workspaceContext ?? throw new ArgumentNullException(nameof(workspaceContext));
        _llmRouter        = llmRouter        ?? throw new ArgumentNullException(nameof(llmRouter));
        _idempotencyStore = idempotencyStore ?? throw new ArgumentNullException(nameof(idempotencyStore));
        _telemetryContext = telemetryContext ?? throw new ArgumentNullException(nameof(telemetryContext));
        _insightEngine    = insightEngine    ?? throw new ArgumentNullException(nameof(insightEngine));
        _insightHistory   = insightHistory   ?? throw new ArgumentNullException(nameof(insightHistory));
        _activityLog      = activityLog      ?? throw new ArgumentNullException(nameof(activityLog));
        _modelCatalog     = modelCatalog     ?? throw new ArgumentNullException(nameof(modelCatalog));
        _providerDefaults = providerDefaults ?? throw new ArgumentNullException(nameof(providerDefaults));
        _rateLimiter      = rateLimiter      ?? throw new ArgumentNullException(nameof(rateLimiter));
        _turnStore             = turnStore            ?? throw new ArgumentNullException(nameof(turnStore));
        _complexityClassifier  = complexityClassifier ?? throw new ArgumentNullException(nameof(complexityClassifier));
        _personaEngine         = personaEngine;

#if DEBUG
        _isDebug = true;
#endif
    }


    public async Task<ConverseResponse> ConverseAsync(ConverseRequest    request
                                                    , CancellationToken ct = default)
    {
        //TODO Reflect on the sheer number of dependencies in this class and consider
        // if we can refactor to reduce coupling and improve testability.
        // For example:
        // - Can we abstract away some of the orchestration steps into separate classes or services?
        // - Can we use a mediator pattern to decouple the components further?
        // This class is doing a lot of work and has many reasons to change,
        // which could lead to maintenance challenges down the line.
        
        // 0. Start telemetry and set a session and initial telemetry 
        var stopwatch = new Stopwatch();
        stopwatch.Start();

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
        
        // 2. Wire up context with necessary references for actions and interpreter to function properly.
// 🔑 Wire meta-actions & LlmActions FIRST
        Actions.MetaActions.SetRegistry(_registry);
        Actions.MetaActions.SetContext(context);
        Actions.MetaActions.SetInsightHistoryStore(_insightHistory);
        Actions.LlmActions.SetContext(context);
        Actions.LlmActions.SetCatalog(_modelCatalog);
        Actions.LlmActions.SetProviderDefaults(_providerDefaults);

// 🔑 Also wire test actions if they might be fast-pathed
        Actions.TestActions.SetContext(context);
        
        // Note: the fast path resolver runs before we persist the model into context.Metadata
        // because some fast path actions might want to make decisions based on the raw user
        // input without the influence of a model.
        // For example,
        // a "Cancel" command should ideally be recognized as such regardless of the model specified in the request.
        
        // EPIC-10-D: Strip workspace prefix and switch workspace before fast-path resolution.
        var resolveInput = request.Input;
        if (_fastPath.TryResolve(request.Input, out var workspaceName, out var workspaceRemainder))
        {
            await _workspaceContext.SetActiveWorkspaceAsync(workspaceName.Name!);
            //resolveInput = workspaceRemainder!;
        }

        if (_fastPath.TryResolve(resolveInput, out var actionMeta, out var fastParams)
            && actionMeta!.IsDestructive.Not())
        {
            await CheckForInsightFollowThroughAsync(actionMeta!.Name, context, ct);
            var fastResponse = await TakeTheFastPath(actionMeta, fastParams, context, ct);

            // ENH-09 / B.4: fire the insight engine on FastPath turns, mirroring the
            // Interpreter+execute path. Record the turn first so providers see the
            // current user message and raw assistant output in context.Turns.
            // Skip the weave pass entirely when the engine returns nothing — FastPath
            // turns must not pay LLM latency when there is nothing to weave.
            context.LastUserMessage = request.Input;

            var initialFastTurn = new ConversationTurn(
                UserMessage:      request.Input ?? string.Empty
              , AssistantMessage: fastResponse.Message ?? string.Empty
              , OccurredAt:       DateTimeOffset.UtcNow
              , Path:             TurnPath.FastPath
              , ActionName:       actionMeta.Name
              , Succeeded:        true);
            context.RecordTurn(initialFastTurn);

            var fastInsights = await SafeGenerateInsightsAsync(context, ct);
            var fastFinalMessage = await ApplyInsightsToResponseAsync(fastResponse.Message ?? string.Empty
                                                                    , fastInsights
                                                                    , context
                                                                    , ct);

            if (fastInsights.Count > 0 && fastFinalMessage != fastResponse.Message)
            {
                context.ReplaceLatestTurn(initialFastTurn with { AssistantMessage = fastFinalMessage });
                fastResponse.Message = fastFinalMessage;
            }

            fastResponse.Insights = fastInsights;

            return await FinalizeAsync(request
                                     , fastResponse
                                     , stopwatch
                                     , TurnPath.FastPath
                                     , actionName: actionMeta.Name
                                     , succeeded:  true
                                     , recordTurn: false
                                     , ct:         ct);
        }

        
        // Persist model into the per-request "model" slot used by LlmInterpreter.
        // Priority: explicit request model > user-set session model > nothing.
        if (request.Model.HasValue())
        {
            context.Metadata["model"] = request.Model!.Trim();
        }
        else if (context.CurrentLlmSession.HasModel)
        {
            context.Metadata["model"] = context.CurrentLlmSession.Model;
        }
        else
        {
            context.Metadata.TryRemove("model", out _);
        }

        // 3. If we are in a clarification flow, consume this turn (Extract to a method?)
        // before we get to the interpreter and treat it as a fast path turn that doesn't require re-interpretation.
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
                    var result     = await _execution.ExecuteAsync(confirmedAction, execParams, context.SessionId, ct);

                    var confirmationResponse = new ConverseResponse
                                               {
                                                       Message = result
                                                     , Debug   = "Delete confirmed and executed."
                                                     , ExecutionResult = $"Executed confirmed action '{confirmedAction.Name}' "
                                                                       + $"with parameters: {string.Join(", ", execParams.Select(pair => $"{pair.Key}={pair.Value}"))}"
                                                     , WasFastPath = true
                                               };
                    return await FinalizeAsync(request
                                             , confirmationResponse
                                             , stopwatch
                                             , TurnPath.Confirmation
                                             , actionName: confirmedAction.Name
                                             , succeeded:  true
                                             , ct:         ct);
                }

                // No match for affirmative, but also not negative:
                // ask for clarification without cancelling the pending action yet
                if (IsNegative(input).Not())
                {
                    var awaiting = new ConverseResponse
                                   {
                                           Message         = "Please confirm or cancel."
                                         , Debug           = $"Awaiting confirmation for '{pending.ActionName}'."
                                         , ExecutionResult = $"User has not yet confirmed or cancelled '{pending.ActionName}'."
                                         , WasFastPath     = true
                                   };
                    return await FinalizeAsync(request
                                             , awaiting
                                             , stopwatch
                                             , TurnPath.Confirmation
                                             , actionName: pending.ActionName
                                             , succeeded:  null
                                             , ct:         ct);
                }

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
                                         , stopwatch
                                         , TurnPath.Confirmation
                                         , actionName: pending.ActionName
                                         , succeeded:  false
                                         , ct:         ct);

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
                return await FinalizeAsync(request
                                         , response
                                         , stopwatch
                                         , TurnPath.Clarification
                                         , actionName: pending.ActionName
                                         , succeeded:  false
                                         , ct:         ct);
            }

            // If somehow no remaining parameters, just execute with what we have
            if (pending.RemainingParameters.Count == 0)
            {
                context.PendingAction = null;

                var finalParameters = ApplyDefaultValues(action, pending.CollectedParameters);
                var execOutput      = await _execution.ExecuteAsync(action, finalParameters, context.SessionId, ct);

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
                return await FinalizeAsync(request
                                         , response
                                         , stopwatch
                                         , TurnPath.Clarification
                                         , actionName: action.Name
                                         , succeeded:  true
                                         , ct:         ct);
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

                return await FinalizeAsync(request
                                         , response
                                         , stopwatch
                                         , TurnPath.Clarification
                                         , actionName: action.Name
                                         , succeeded:  null
                                         , ct:         ct);
            }

            // execute the action now
            context.PendingAction = null;

            var parameters  = ApplyDefaultValues(action, pending.CollectedParameters);
            var finalOutput = await _execution.ExecuteAsync(action, parameters, context.SessionId, ct);

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
            return await FinalizeAsync(request
                                     , finalClarificationResponse
                                     , stopwatch
                                     , TurnPath.Clarification
                                     , actionName: action.Name
                                     , succeeded:  true
                                     , ct:         ct);
        }

        // Persona pre-pass: resolve intent and optionally apply a personality-specific model
        // for this turn only. Runs only on the LLM interpreter path; fast-path and clarification
        // turns have already returned above. If the engine is absent or resolves Unknown / null
        // personality, the existing active config is used unchanged.
        await ApplyPersonaPrePassAsync(request.Input, context, request.Model.HasValue(), ct);

        // 4. Log interpreter identity
        _telemetry.Track(_telemetryContext.CreateEvent(new OrchestratorProgressEvent
                                                       {
                                                               Details = $"Interpreter.Selected; Using interpreter: {_interpreter.GetType().Name}"
                                                       }));
        
        // Steps 5 & 6: Run the interpreter and handle failures like missing parameters or no action recognized.
        
        // TODO: Define `WasResolvedFor` first
        // Debug.Assert(!_fastPath.WasResolvedFor(request),
        //              "Interpreter should not run after FastPath resolution.");

        // ENH-19 Phase B: classify task complexity post-FastPath so the router
        // can pick a model tier appropriate for the work. FastPath turns have
        // already returned above, so classification here is the only path that
        // pays the router-tier signal.
        var complexity     = _complexityClassifier.Classify(request.Input);
        var interpretation = await _interpreter.InterpretWithContext(request.Input, context, complexity);
       
        
        // 5. Log interpreter outcome and details into telemetry and context for
        // downstream use in execution, insights, and future interpretation.
        
        // 5a. By default, clear any pending action; clarification will set it again
        context.PendingAction = null;

        // 5b. Persist interpreter decision (success or failure) into context
        context.LastUserMessage          = request.Input;
        context.LastInterpreterName      = _interpreter.GetType().Name;
        context.LastActionName           = interpretation.ActionName;
        context.LastInterpreterReason    = interpretation.Reason;
        context.LastInterpreterDebug     = interpretation.DebugInfo;
        context.LastFailureType          = interpretation.FailureType;
        context.LastInterpreterException = interpretation.Exception;
        
        if (interpretation.FailureType == InterpreterFailureType.Exception)
        {
            // BUG-20 / EPIC-07-B: user-friendly message for 429 and capacity errors.
            // Debug detail is appended inside BuildExceptionMessage for generic failures only
            // so that 429 / capacity-exhaustion messages are never overridden.
            var message = BuildExceptionMessage(interpretation.Exception
                                              , interpretation.DebugInfo ?? string.Empty);

            var llmExceptionResponse = new ConverseResponse
                                       {
                                               Message         = message
                                             , Debug           = interpretation.DebugInfo
                                             , ExecutionResult = $"Interpreter reason: {interpretation.Reason}"
                                             , Success         = false
                                       };
            return await FinalizeAsync(request
                                     , llmExceptionResponse
                                     , stopwatch
                                     , TurnPath.Interpreter
                                     , actionName: null
                                     , succeeded:  false
                                     , ct:         ct);
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
        
        // End 5. Log interpreter outcome and details

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
                return await  FinalizeAsync(request
                                          , response
                                          , stopwatch
                                          , TurnPath.Interpreter
                                          , actionName: interpretation.ActionName
                                          , succeeded:  false
                                          , ct:         ct);
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

                if (action.Name.IsEqualTo("StoreValue")
                 && firstMissing.IsEqualTo("key"))
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
                return await  FinalizeAsync(request
                                          , response
                                          , stopwatch
                                          , TurnPath.Interpreter
                                          , actionName: action.Name
                                          , succeeded:  null
                                          , ct:         ct);
            }

            // Action does NOT allow clarification: treat as a normal failure
            var missingJoined = string.Join(", ", interpretation.MissingParameters);

            //TODO: for better UX, we could potentially distinguish between
            // "I understood the command but I'm missing details" vs.
            // "I didn't understand the command at all and also couldn't find
            //  any close matches that would allow me to ask a clarification question".
            // The former is what we're doing here; the latter might be a more generic
            // "I didn't understand that at all, here are some things you can try" message.🤷‍♂️
            var message = BuildMissingParametersMessage(interpretation);

            var missingParametersResponse = new ConverseResponse
                                            {
                                                    Message         = message
                                                  , Debug           = $"Missing required parameters for action '{interpretation.ActionName}': {missingJoined}"
                                                  , ExecutionResult = $"Interpreter reason: {interpretation.Reason}"
                                            };
            return await FinalizeAsync(request
                                     , missingParametersResponse
                                     , stopwatch
                                     , TurnPath.Interpreter
                                     , actionName: interpretation.ActionName
                                     , succeeded:  false
                                     , ct:         ct);
        }
        
        // 7. No action chosen at all (e.g. nonsense input or other failure)
        
        if (interpretation.ActionName?.HasNoValue() ?? true)
        {
            //TODO: Should the `ChitChat` action be interpreted as "no action chosen"
            // or should it be a valid action choice that just happens to be conversational?
        
            var message = BuildNoActionMessage(interpretation);
            var missingActionResponse = new ConverseResponse
                                        {
                                                Message = message
                                              , Debug   = interpretation.DebugInfo
                                        };
            return await FinalizeAsync(request
                                     , missingActionResponse
                                     , stopwatch
                                     , TurnPath.Interpreter
                                     , actionName: null
                                     , succeeded:  false
                                     , ct:         ct);
        }

        // Step 8 setup for Execution:
        // we have an action name from the interpreter,
        // but we still need to look up the metadata so we know which method to call and
        // what parameters it needs.
        
        // 8. Look up the action reflectively
        var selectedAction = _registry.Actions
                                      .FirstOrDefault(metadata => string.Equals(metadata.Name
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
            return await FinalizeAsync(request
                                     , unknownActionResponse
                                     , stopwatch
                                     , TurnPath.Interpreter
                                     , actionName: interpretation.ActionName
                                     , succeeded:  false
                                     , ct:         ct);

        }

        // Generic confirmation gate: any action decorated with [DestructiveAction]
        // requires explicit user confirmation before execution.
        if (selectedAction.IsDestructive)
        {
            var confirmationMessage = BuildDestructiveConfirmationPrompt(selectedAction
                                                                       , interpretation.ExtractedParameters);

            var collectedParameters = new Dictionary<string, string>(interpretation.ExtractedParameters
                                                                   , StringComparer.OrdinalIgnoreCase);
            context.PendingAction = new PendingAction
                                    {
                                            ActionName           = selectedAction.Name
                                          , CollectedParameters  = collectedParameters
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
            return await FinalizeAsync(request
                                     , response
                                     , stopwatch
                                     , TurnPath.Confirmation
                                     , actionName: selectedAction.Name
                                     , succeeded:  null
                                     , ct:         ct);
        }

        // 9. Execute with whatever parameters we have (including defaults for optionals)
        var execParameters  = ApplyDefaultValues(selectedAction, interpretation.ExtractedParameters);
        await CheckForInsightFollowThroughAsync(selectedAction.Name, context, ct);
        var execOutputFinal = await _execution.ExecuteAsync(selectedAction, execParameters, context.SessionId, ct);

        // ENH-08: record the turn BEFORE the engine fires so providers see the current
        // user message + raw assistant output in context.Turns. After weave, replace the
        // latest entry with the woven message so the history matches what the user saw.
        // Phase A's Insight Engine integration only runs on this path; FinalizeAsync below
        // is told recordTurn:false because we did the recording inline here.
        var initialTurn = new ConversationTurn(UserMessage:      request.Input ?? string.Empty
                                             , AssistantMessage: execOutputFinal
                                             , OccurredAt:       DateTimeOffset.UtcNow
                                             , Path:             TurnPath.Interpreter
                                             , ActionName:       selectedAction.Name
                                             , Succeeded:        true);
        context.RecordTurn(initialTurn);

        // 10. Store execution result in context for insights and future interpretation,
        // then run Finalize and return
        // Insight Engine — runs after execution; only pays LLM cost when insights exist.
        // Failure isolation: a faulted engine call never breaks the turn; the response
        // falls back to the raw execution result with no insights attached.
        var insights = await SafeGenerateInsightsAsync(context, ct);
        var finalMessage = await ApplyInsightsToResponseAsync(execOutputFinal
                                                            , insights
                                                            , context
                                                            , ct);

        if (insights.Count > 0 
         && finalMessage != execOutputFinal)
        {
            // Weave produced a different (woven) message — swap the latest turn so the
            // recorded AssistantMessage matches what the user actually saw.
            context.ReplaceLatestTurn(initialTurn with { AssistantMessage = finalMessage });
        }

        // ENH-19: if a Heavy LLM call was downgraded during execution (e.g., IdentityAnalysisService),
        // the capacity router stores a note in context.Metadata.  Surface it to the user once.
        if (context.Metadata.TryGetValue("tier_downgrade_note", out var downgradeNote)
         && !string.IsNullOrEmpty(downgradeNote))
        {
            finalMessage += $"\n\n_{downgradeNote}_";
            context.Metadata.TryRemove("tier_downgrade_note", out _);
        }

        var finalResponse = new ConverseResponse
                            {
                                    Message         = finalMessage
                                  , Insights        = insights
                                  , Debug           = interpretation.DebugInfo
                                  , ExecutionResult = $"Executed action '{selectedAction.Name}' with parameters: {string.Join(", ", execParameters.Select(pair => $"{pair.Key}={pair.Value}"))}"
                            };

        return await FinalizeAsync(request
                                 , finalResponse
                                 , stopwatch
                                 , TurnPath.Interpreter
                                 , actionName: selectedAction.Name
                                 , succeeded:  true
                                 , recordTurn: false
                                 , ct:         ct);
    }

    
    public async Task<ConverseResponse> FinalizeAsync( ConverseRequest   request
                                                      , ConverseResponse  response
                                                      , Stopwatch         sw
                                                      , TurnPath          path
                                                      , string?           actionName = null
                                                      , bool?             succeeded  = null
                                                      , bool              recordTurn = true
                                                      , CancellationToken ct         = default )
    {
        // ENH-08: append the turn to the session's bounded history.
        // The Interpreter+execute path records inline (around the engine call) so it can
        // capture the un-woven message before the engine fires; it passes recordTurn:false
        // here to avoid double-recording.
        if (recordTurn)
        {
            var context = _contextStore.GetOrCreate(request.SessionId);
            context.RecordTurn(new ConversationTurn(
                                       UserMessage:      request.Input ?? string.Empty
                                     , AssistantMessage: response.Message ?? string.Empty
                                     , OccurredAt:       DateTimeOffset.UtcNow
                                     , Path:             path
                                     , ActionName:       actionName
                                     , Succeeded:        succeeded));
        }

        // ENH-20: persist the finalised turn so history survives server restarts.
        // In recordTurn:false paths the in-memory turn was already recorded (and possibly
        // replaced after insight weaving) before FinalizeAsync was called; in recordTurn:true
        // paths it was just added above. Either way, the last entry in context.Turns is the
        // canonical, fully-woven turn to store.
        var finalContext = _contextStore.GetOrCreate(request.SessionId);
        var latestTurn   = finalContext.Turns.LastOrDefault();
        if (latestTurn is not null)
            await _turnStore.SaveAsync(request.SessionId, latestTurn, ct);

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

        return response;
    }
    
    private async Task<IReadOnlyList<Insight>> SafeGenerateInsightsAsync(
        ConversationContext context
      , CancellationToken   ct )
    {
        try
        {
            return await _insightEngine.GenerateInsightsAsync(context, ct)
                ?? Array.Empty<Insight>();
        }
        catch (Exception ex)
        {
            // Engine should swallow provider faults itself; reaching here means a
            // structural failure (DI, history store, activity log). Don't break the turn.
            await _activityLog.LogAsync(new Domains.Activity.ActivityEvent
                                        {
                                                ActivityType = InsightActivityTypes.ProviderFailed
                                              , Notes        = $"Engine: {ex.GetType().Name}: {ex.Message}"
                                              , Meta         = new Dictionary<string, string>
                                                               {
                                                                       ["scope"] = "engine"
                                                               }
                                        }, ct);

            return Array.Empty<Insight>();
        }
    }

    private async Task<string> ApplyInsightsToResponseAsync(
        string                 execOutput
      , IReadOnlyList<Insight> insights
      , ConversationContext    context
      , CancellationToken      ct )
    {
        if (insights.Count == 0)
        {
            context.SetLastEmittedInsights(Array.Empty<EmittedInsightRef>());
            return execOutput;
        }

        await _insightHistory.RecordEmittedAsync(insights, ct);

        var refs = insights
            .Select(insight => new EmittedInsightRef(insight.DeduplicationKey
                                                   , insight.SuggestedAction))
            .ToList();
        context.SetLastEmittedInsights(refs);

        // Weave failure: log + fall back to un-woven message. Structured insights
        // remain on the response so clients that render them distinctly still see them.
        try
        {
            return await _llmRouter.WeaveAsync(context, execOutput, insights, ct);
        }
        catch (Exception ex)
        {
            await _activityLog.LogAsync(new Domains.Activity.ActivityEvent
                                        {
                                                ActivityType = InsightActivityTypes.WeaveFailed
                                              , Notes        = $"{ex.GetType().Name}: {ex.Message}"
                                              , Meta         = new Dictionary<string, string>
                                                               {
                                                                       ["insightCount"] = insights.Count.ToString()
                                                               }
                                        }, ct);

            return execOutput;
        }
    }

    private async Task<ConverseResponse> TakeTheFastPath( ActionMetadata?             actionMeta
                                                        , Dictionary<string, string>? fastParams
                                                        , ConversationContext         context
                                                        , CancellationToken           ct = default )
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

        var result = await _execution.ExecuteAsync(actionMeta!, fastParams!, context.SessionId, ct);

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

        // Persist model into the per-request "model" slot (same priority as ConverseAsync)
        if (request.Model.HasValue())
        {
            context.Metadata["model"] = request.Model.Trim();
        }
        else if (context.CurrentLlmSession.HasModel)
        {
            context.Metadata["model"] = context.CurrentLlmSession.Model;
        }
        else
        {
            context.Metadata.TryRemove("model", out _);
        }

        // Wire context (same as ConverseAsync)
        Actions.TestActions.SetContext(context);
        Actions.MetaActions.SetRegistry(_registry);
        Actions.MetaActions.SetContext(context);
        Actions.MetaActions.SetInsightHistoryStore(_insightHistory);
        Actions.LlmActions.SetContext(context);
        Actions.LlmActions.SetCatalog(_modelCatalog);
        Actions.LlmActions.SetProviderDefaults(_providerDefaults);

        // ✅ J-01.1: FastPath always wins (for non-destructive actions).
        // In streaming mode: if FastPath resolves a non-destructive action, execute
        // and emit a single chunk. Destructive actions fall through to the interpreter.
        // EPIC-10-D: Strip workspace prefix and switch workspace before fast-path resolution.
        var streamResolveInput = request.Input;
        if (_fastPath.TryExtractWorkspacePrefix(request.Input, out var streamWorkspaceName, out var streamWorkspaceRemainder))
        {
            await _workspaceContext.SetActiveWorkspaceAsync(streamWorkspaceName!);
            streamResolveInput = streamWorkspaceRemainder!;
        }

        if (_fastPath.TryResolve(streamResolveInput, out var actionMeta, out var fastParams)
            && actionMeta!.IsDestructive == false)
        {
            _telemetry.Track(_telemetryContext.CreateEvent(new OrchestratorProgressEvent
                                                   {
                                                           Details = $"FastPath.Resolved.Stream; Action={actionMeta!.Name}"
                                                   }));

            var result = await _execution.ExecuteAsync(actionMeta!, fastParams!, context.SessionId, ct);
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
        var streamComplexity = _complexityClassifier.Classify(request.Input);
        var interpretation   = await _interpreter.InterpretWithContext(request.Input, context, streamComplexity);

        // 🚫 If an action was selected, DO NOT stream
        if (interpretation.ActionName.HasValue())
        {
            var result = await ConverseAsync(request, ct);
            
            yield return result.Message ?? string.Empty;
            yield break;
        }

        // 🔥 Stream directly from the LLM.
        // Router reads context.Metadata to pick the active provider + model —
        // the "model" key was already populated above based on request/session priority.
        await foreach (var chunk in _llmRouter.StreamAsync(request.Input
                                                         , context
                                                         , ct))
        {
            yield return chunk;
        }
        
        //TODO:  Figure out how to determine when the stream is complete.
        // Does this need to be determined in the controller?  
    }

    // BUG-20 / EPIC-07-B: returns a human-friendly message for 429 and capacity-exhaustion errors.
    // User-friendly exception types are returned as-is even in debug mode so tests are reliable.
    private string BuildExceptionMessage(Exception? exception, string debugInfo = "")
    {
        if (exception is LlmCapacityExceededException)
            return "All AI providers are currently at capacity. Please try again later.";

        if (exception?.Message.Contains("429", StringComparison.Ordinal) == true)
        {
            var snapshot  = _rateLimiter.GetLatest("Groq");
            var resetPart = snapshot.RequestsResetAt is not null
                                    ? $" — resets at {snapshot.RequestsResetAt.Value.ToLocalTime():h:mm tt}"
                                    : string.Empty;
            return $"Groq rate limit reached{resetPart}. Please wait a moment before trying again.";
        }

        if (_isDebug)
            return $"""
                    ## Something went wrong while processing your request.
                    ----
                    You are getting this because:
                    ```csharp
                    interpretation.FailureType == InterpreterFailureType.Exception
                    {exception?.ToString() ?? "No exception details available."}
                    ```
                    Is `true`
                    The exception is:
                    >{debugInfo}
                    """;

        return "Something went wrong while processing your request. Please try again.";
    }

    private string BuildNoActionMessage(InterpreterResult interpretation)
    {
        var candidates = interpretation.CandidateActions is { Count: > 0 }
                                 ? string.Join(", ", interpretation.CandidateActions)
                                 : null;

        var normalMessage = candidates is not null
                                    ? $"I wasn't sure what you meant — possibly '{candidates}'? Type 'what can you do' to see all available commands."
                                    : "I didn't recognize that as a command. Type 'what can you do' to see all available commands.";

        if (_isDebug)
        {
            return $"""
                    {normalMessage}

                    [DEBUG] Reason: {interpretation.Reason}
                    """;
        }

        return normalMessage;
    }

    private string BuildMissingParametersMessage(InterpreterResult interpretation)
    {
        if (_isDebug)
        {
            var missingJoinedDebug = string.Join(", ", interpretation.MissingParameters ?? []);
            return $$"""
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
                    Missing: {{missingJoinedDebug}}
                    """;
        }

        var firstMissing = interpretation.MissingParameters is { Count: > 0 }
                                   ? interpretation.MissingParameters[0]
                                   : null;

        var detail = firstMissing is not null
                             ? $" I need a value for '{firstMissing}'."
                             : string.Empty;

        return $"I know what you want to do, but I'm missing some required details.{detail} Please try again with the full details.";
    }

    /// <summary>
    /// Records <see cref="InsightOutcome.ActedOn"/> when the user's resolved action
    /// matches a <see cref="EmittedInsightRef.SuggestedAction"/> from the previous turn,
    /// then clears the emitted-insight snapshot so the same signal is not double-counted.
    /// Must be called after action resolution but before execution so the outcome is
    /// persisted regardless of whether execution succeeds.
    /// </summary>
    private async Task CheckForInsightFollowThroughAsync( string             resolvedActionName
                                                        , ConversationContext context
                                                        , CancellationToken  ct )
    {
        var match = context.LastEmittedInsights
            .FirstOrDefault(emitted => string.Equals(emitted.SuggestedAction
                                                    , resolvedActionName
                                                    , StringComparison.OrdinalIgnoreCase));

        if (match is not null)
        {
            await _insightHistory.RecordOutcomeAsync(match.DeduplicationKey
                                                   , InsightOutcome.ActedOn
                                                   , ct);
            context.SetLastEmittedInsights(Array.Empty<EmittedInsightRef>());
        }
    }

    private async Task ApplyPersonaPrePassAsync(
        string              userMessage
      , ConversationContext context
      , bool               requestHasPinnedModel
      , CancellationToken   ct)
    {
        if (_personaEngine is null)
            return;

        // Never override a model the caller explicitly pinned for this request.
        if (requestHasPinnedModel)
            return;

        try
        {
            var personaResult = await _personaEngine.ResolveAsync(userMessage, ct).ConfigureAwait(false);

            if (personaResult.Intent == Domains.PersonaEngine.Intent.Unknown
             || personaResult.Personality is null)
            {
                return;
            }

            var modelConfig = personaResult.Personality.ModelConfig;

            if (modelConfig is null)
                return;

            context.Metadata.TryGetValue("model", out var currentModel);

            if (modelConfig.ModelId is not null
             && !string.Equals(modelConfig.ModelId, currentModel, StringComparison.OrdinalIgnoreCase))
            {
                context.Metadata["model"] = modelConfig.ModelId;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Persona pre-pass failures must never break the turn.
            // Cancellation is allowed to propagate so the caller's CancellationToken is honoured.
        }
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
}