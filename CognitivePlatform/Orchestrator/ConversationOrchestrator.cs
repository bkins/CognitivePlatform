using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CognitivePlatform.Api.Avails;
using CognitivePlatform.Api.Controllers;
using CognitivePlatform.Api.Conversation;
using CognitivePlatform.Api.Execution;
using CognitivePlatform.Api.Interpreter;
using CognitivePlatform.Api.Models;
using CognitivePlatform.Api.Registry;
using CognitivePlatform.Api.Telemetry;

namespace CognitivePlatform.Api.Orchestrator;

public class ConversationOrchestrator : IConversationOrchestrator
{
    private readonly IActionRegistry          _registry;
    private readonly IInterpreter             _interpreter;   // Keyed: LlmInterpreter
    private readonly IExecutionEngine         _execution;
    private readonly ConversationContextStore _contextStore;
    private readonly ITelemetrySink           _telemetry;

    public ConversationOrchestrator(
        IActionRegistry          registry,
        [FromKeyedServices(KeyedServices.LlmInterpreter)]
        IInterpreter             interpreter,
        IExecutionEngine         execution,
        ConversationContextStore contextStore,
        ITelemetrySink           telemetry)
    {
        _registry     = registry     ?? throw new ArgumentNullException(nameof(registry));
        _interpreter  = interpreter  ?? throw new ArgumentNullException(nameof(interpreter));
        _execution    = execution    ?? throw new ArgumentNullException(nameof(execution));
        _contextStore = contextStore ?? throw new ArgumentNullException(nameof(contextStore));
        _telemetry    = telemetry    ?? throw new ArgumentNullException(nameof(telemetry));
    }

    public async Task<ConverseResponse> ConverseAsync(
        ConverseRequest   request,
        CancellationToken ct = default)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));

        _telemetry.Track("Conversation.Start", $"Input='{request.Input}'");

        // 1. Get or create the session context
        var context = _contextStore.GetOrCreate(request.SessionId);

        // 2. Wire it into the test actions (needed for StoreValue, RecallValue, RepeatLastAction)
        Actions.TestActions.SetContext(context);

        // 2b. Wire the registry into meta-actions so they can introspect available actions
        Actions.MetaActions.SetRegistry(_registry);

        // 2c. Wire context into meta-actions so they can explain reasoning
        Actions.MetaActions.SetContext(context);

        // 3. If we are in a clarification flow, consume this turn
        if (context.PendingAction is not null)
        {
            var pending = context.PendingAction;

            // Look up action metadata
            var action = _registry.Actions.FirstOrDefault(a =>
                string.Equals(a.Name, pending.ActionName, StringComparison.OrdinalIgnoreCase));

            if (action is null)
            {
                // Safety: clear the pending action so we don't get stuck
                context.PendingAction = null;

                const string msg = "The action I was trying to clarify is no longer available.";
                _telemetry.Track("Clarification.ActionMissing", msg);

                return new ConverseResponse
                {
                    Message = msg,
                    Debug   = "Pending action not found in registry."
                };
            }

            // If somehow no remaining parameters, just execute with what we have
            if (pending.RemainingParameters.Count == 0)
            {
                context.PendingAction = null;

                var execOutput = _execution.Execute(action, pending.CollectedParameters);

                _telemetry.Track("Clarification.Completed",
                    $"Action={pending.ActionName}, Collected={pending.CollectedParameters.Count}");

                return new ConverseResponse
                {
                    Message = execOutput,
                    Debug   = $"Executed pending action '{pending.ActionName}' with previously collected parameters."
                };
            }

            // Take the next missing parameter name
            var nextParameterName = pending.RemainingParameters[0];

            // Whatever the user typed on this turn becomes the value for that parameter.
            var userValue = request.Input ?? string.Empty;

            pending.CollectedParameters[nextParameterName] = userValue;
            pending.RemainingParameters.RemoveAt(0);

            _telemetry.Track("Clarification.ParameterCollected",
                $"Action={pending.ActionName}, Parameter={nextParameterName}, Value='{userValue}'");

            // If there are still parameters to collect, ask for the next one
            if (pending.RemainingParameters.Count > 0)
            {
                var followingName = pending.RemainingParameters[0];

                var paramMeta = action.Parameters.FirstOrDefault(p =>
                    string.Equals(p.Name, followingName, StringComparison.OrdinalIgnoreCase));

                var friendlyName = string.IsNullOrWhiteSpace(paramMeta?.Description)
                    ? followingName
                    : paramMeta.Description;

                var question = pending.CollectedParameters.Count == 1
                    ? $"Got it. Now I need a value for '{friendlyName}'. What should it be?"
                    : $"Still need a value for '{friendlyName}'. What should it be?";

                context.PendingAction = pending;

                return new ConverseResponse
                {
                    Message = question,
                    Debug   = $"Clarification: collected '{nextParameterName}' = '{userValue}'. Still need parameter '{friendlyName}'."
                };
            }

            // Otherwise we have all parameters: execute the action now
            context.PendingAction = null;

            var finalOutput = _execution.Execute(action, pending.CollectedParameters);

            _telemetry.Track("Clarification.Completed",
                $"Action={pending.ActionName}, Collected={pending.CollectedParameters.Count}");

            return new ConverseResponse
            {
                Message = finalOutput,
                Debug   = $"Executed pending action '{pending.ActionName}' after collecting all required parameters."
            };
        }

        // 4. Log interpreter identity
        _telemetry.Track("Interpreter.Selected",
            $"Using interpreter: {_interpreter.GetType().Name}");

        // 5. Interpret with context
        var interpretation = await _interpreter.InterpretWithContext(
            request.Input,
            context);

        // 5a. By default, clear any pending action; clarification will set it again
        context.PendingAction = null;

        // 5b. Persist interpreter decision (success or failure) into context
        context.LastUserMessage       = request.Input;
        context.LastActionName        = interpretation.ActionName;
        context.LastInterpreterReason = interpretation.Reason;
        context.LastInterpreterDebug  = interpretation.DebugInfo;
        context.LastFailureType       = interpretation.FailureType;

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
        if (interpretation.FailureType == InterpreterFailureType.MissingParameters
            && interpretation.ActionName is not null
            && interpretation.MissingParameters is { Count: > 0 })
        {
            // Look up action metadata
            var action = _registry.Actions.FirstOrDefault(metadata =>
                string.Equals(metadata.Name,
                    interpretation.ActionName,
                    StringComparison.OrdinalIgnoreCase));

            if (action is null)
            {
                var msg = $"Interpreter selected unknown action '{interpretation.ActionName}'.";
                _telemetry.Track("ActionLookup.Failed", msg);

                return new ConverseResponse
                {
                    Message = "That action is not registered in this system.",
                    Debug   = msg
                };
            }

            if (action.AllowsClarification)
            {
                // Normalize missing parameter names
                var missingNames = interpretation.MissingParameters
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Select(n => n.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                // Special handling for StoreValue: always ask for "key" first, then "value"
                if (action.Name.Equals("StoreValue", StringComparison.OrdinalIgnoreCase))
                {
                    var ordered = new List<string>();

                    if (missingNames.Any(n => n.Equals("key", StringComparison.OrdinalIgnoreCase)))
                        ordered.Add("key");

                    if (missingNames.Any(n => n.Equals("value", StringComparison.OrdinalIgnoreCase)))
                        ordered.Add("value");

                    // Fall back to whatever we got if something unexpected happens
                    if (ordered.Count > 0)
                        missingNames = ordered;
                }

                var firstMissing = missingNames[0];

                var paramMeta = action.Parameters.FirstOrDefault(p =>
                    string.Equals(p.Name, firstMissing, StringComparison.OrdinalIgnoreCase));

                var friendlyName = string.IsNullOrWhiteSpace(paramMeta?.Description)
                    ? firstMissing
                    : paramMeta.Description;

                string question;

                if (action.Name.Equals("StoreValue", StringComparison.OrdinalIgnoreCase)
                    && firstMissing.Equals("key", StringComparison.OrdinalIgnoreCase))
                {
                    // Very explicit wording so the user knows the next utterance becomes the literal key
                    question =
                        "I can store that value, but I need the literal key to store it under. " +
                        "Please tell me exactly what key to use next – whatever you type will be used verbatim as the key.";
                }
                else
                {
                    question = $"I can run {action.Name}, but I need a value for '{friendlyName}'. What should it be?";
                }

                context.PendingAction = new PendingAction
                {
                    ActionName          = action.Name,
                    CollectedParameters = new Dictionary<string, string>(
                        interpretation.ExtractedParameters,
                        StringComparer.OrdinalIgnoreCase),
                    RemainingParameters = missingNames
                };

                return new ConverseResponse
                {
                    Message = question,
                    Debug   = interpretation.DebugInfo
                };
            }

            // Action does NOT allow clarification: treat as a normal failure
            var missingJoined = string.Join(", ", interpretation.MissingParameters);
            return new ConverseResponse
            {
                Message = "I'm not sure what to do next.",
                Debug   = $"Missing required parameters for action '{interpretation.ActionName}': {missingJoined}"
            };
        }

        // 7. No action chosen at all (e.g. nonsense input or other failure)
        if (interpretation.ActionName is null)
        {
            return new ConverseResponse
            {
                Message = "I'm not sure what to do next.",
                Debug   = interpretation.DebugInfo
            };
        }

        // 8. Look up the action reflectively
        var selectedAction = _registry.Actions.FirstOrDefault(metadata =>
            string.Equals(metadata.Name,
                interpretation.ActionName,
                StringComparison.OrdinalIgnoreCase));

        if (selectedAction is null)
        {
            var msg = $"Interpreter selected unknown action '{interpretation.ActionName}'.";
            _telemetry.Track("ActionLookup.Failed", msg);

            return new ConverseResponse
            {
                Message = "That action is not registered in this system.",
                Debug   = msg
            };
        }

        // 9. Execute (sync) with whatever parameters we have
        var execOutputFinal = _execution.Execute(selectedAction, interpretation.ExtractedParameters);

        // 10. Return consolidated response (context has already been updated above)
        return new ConverseResponse
        {
            Message = execOutputFinal,
            Debug   = interpretation.DebugInfo
        };
    }
}


// using CognitivePlatform.Api.Avails;
// using CognitivePlatform.Api.Controllers;
// using CognitivePlatform.Api.Conversation;
// using CognitivePlatform.Api.Execution;
// using CognitivePlatform.Api.Interpreter;
// using CognitivePlatform.Api.Models;
// using CognitivePlatform.Api.Registry;
// using CognitivePlatform.Api.Telemetry;
//
// namespace CognitivePlatform.Api.Orchestrator;
//
// public class ConversationOrchestrator : IConversationOrchestrator
// {
//     private readonly IActionRegistry          _registry;
//     private readonly IInterpreter             _interpreter;   // Keyed: LlmInterpreter
//     private readonly IExecutionEngine         _execution;
//     private readonly ITelemetrySink           _telemetry;
//     private readonly ConversationContextStore _contextStore;
//
//     public ConversationOrchestrator (IActionRegistry                                                registry
//                                    , [FromKeyedServices(KeyedServices.LlmInterpreter)] IInterpreter interpreter
//                                    , IExecutionEngine                                               execution
//                                    , ITelemetrySink                                                 telemetry
//                                    , ConversationContextStore                                       contextStore)
//     {
//         _registry     = registry;
//         _interpreter  = interpreter;
//         _execution    = execution;
//         _telemetry    = telemetry;
//         _contextStore = contextStore;
//     }
//
//     public async Task<ConverseResponse> ConverseAsync (ConverseRequest   request
//                                                      , CancellationToken ct = default)
//     {
//         _telemetry.Track("Conversation.Start"
//                        , $"Input='{request.Input}'");
//
//         // 1. Get or create the session context
//         var context = _contextStore.GetOrCreate(request.SessionId);
//
//         // 2. Wire it into the test actions (needed for StoreValue, RecallValue, RepeatLastAction)
//         Actions.TestActions.SetContext(context);
//
//         // 2b. Wire the registry into meta-actions so they can introspect available actions
//         Actions.MetaActions.SetRegistry(_registry);
//
//         // 2c. Wire context into meta-actions so they can explain reasoning
//         Actions.MetaActions.SetContext(context);
//
//         // ---------------------------------------------------------------------
//         // 3. Clarification for a pending multi-turn action
//         // ---------------------------------------------------------------------
//         if (context.PendingAction is not null)
//         {
//             var pending = context.PendingAction;
//
//             // Determine which parameter we're collecting now
//             var paramName = pending.RemainingParameters[0];
//
//             // Clean the user's input (strip outer quotes)
//             var cleaned = NormalizeUserValue(request.Input);
//
//             // Store the collected parameter
//             pending.CollectedParameters[paramName] = cleaned;
//
//             // Remove this parameter from the remaining list
//             pending.RemainingParameters.RemoveAt(0);
//
//             // Do we still need more parameters?
//             if (pending.RemainingParameters.Count > 0)
//             {
//                 var nextParam = pending.RemainingParameters[0];
//
//                 // Look up friendly name & metadata
//                 var actionMeta = _registry.Actions.First(a => a.Name == pending.ActionName);
//                 var paramMeta = actionMeta.Parameters.First(p => p.Name.Equals(nextParam
//                                                                              , StringComparison.OrdinalIgnoreCase));
//
//                 var friendlyName = string.IsNullOrWhiteSpace(paramMeta.Description)
//                                            ? paramMeta.Name
//                                            : paramMeta.Description;
//
//                 context.PendingAction = pending;
//
//                 return new ConverseResponse
//                        {
//                                Message = $"Got it. Now I need a value for '{friendlyName}'. What should it be?"
//                              , Debug   = $"Clarification: collected '{paramName}' = '{cleaned}'. Still need parameter '{nextParam}'."
//                        };
//             }
//
//             // Otherwise all parameters collected → run the action
//             var actionToRun = _registry.Actions.First(a => a.Name == pending.ActionName);
//             var finalParams = new Dictionary<string, string>(pending.CollectedParameters
//                                                            , StringComparer.OrdinalIgnoreCase);
//
//             var output = _execution.Execute(actionToRun
//                                           , finalParams);
//
//             // Clear pending state
//             context.PendingAction = null;
//
//             return new ConverseResponse
//                    {
//                            Message = output
//                          , Debug = $"Executed pending action '{actionToRun.Name}' after collecting all required parameters."
//                    };
//         }
//
//         // ---------------------------------------------------------------------
//         // 4. Normal interpreter path (no pending action)
//         // ---------------------------------------------------------------------
//
//         // Starting a fresh interpretation; clear any stale pending action
//         context.PendingAction = null;
//
//         // 4a. Log interpreter identity
//         _telemetry.Track("Interpreter.Selected"
//                        , $"Using interpreter: {_interpreter.GetType().Name}");
//
//         // 4b. Interpret with context
//         var interpretation = await _interpreter.InterpretWithContext(request.Input
//                                                                    , context);
//
//         // 4c. Persist interpreter decision (success or failure) into context
//         context.LastUserMessage       = request.Input;
//         context.LastActionName        = interpretation.ActionName;
//         context.LastInterpreterReason = interpretation.Reason;
//         context.LastInterpreterDebug  = interpretation.DebugInfo;
//         context.LastFailureType       = interpretation.FailureType;
//
//         context.LastCandidateActions.Clear();
//         if (interpretation.CandidateActions is { Count: > 0 })
//         {
//             foreach (var name in interpretation.CandidateActions)
//                 context.LastCandidateActions.Add(name);
//         }
//
//         context.LastMissingParameters.Clear();
//         if (interpretation.MissingParameters is { Count: > 0 })
//         {
//             foreach (var missingItem in interpretation.MissingParameters)
//                 context.LastMissingParameters.Add(missingItem);
//         }
//
//         context.LastParameters.Clear();
//         foreach (var pair in interpretation.ExtractedParameters)
//             context.LastParameters[pair.Key] = pair.Value;
//
//         // 4d. Handle the "missing required parameters" failure case
//         if (interpretation is
//             {
//                     FailureType: InterpreterFailureType.MissingParameters
//                   , ActionName: not null
//                   , MissingParameters.Count: > 0
//             })
//         {
//             // Look up action metadata
//             var action = _registry.Actions.FirstOrDefault(metadata => string.Equals(metadata.Name
//                                                                                   , interpretation.ActionName
//                                                                                   , StringComparison.OrdinalIgnoreCase));
//
//             if (action is null)
//             {
//                 var msg = $"Interpreter selected unknown action '{interpretation.ActionName}'.";
//                 
//                 _telemetry.Track("ActionLookup.Failed"
//                                , msg);
//
//                 return new ConverseResponse
//                        {
//                                Message = "That action is not registered in this system."
//                              , Debug = msg
//                        };
//             }
//
//             if (action.AllowsClarification)
//             {
//                 // For now we ask about one parameter at a time.
//                 var missingNames = interpretation.MissingParameters.ToList();
//                 var firstMissing = missingNames[0];
//
//                 var paramMeta = action.Parameters.FirstOrDefault(parameter => string.Equals(parameter.Name
//                                                                                           , firstMissing
//                                                                                           , StringComparison.OrdinalIgnoreCase));
//
//                 var friendlyName = string.IsNullOrWhiteSpace(paramMeta?.Description)
//                                            ? firstMissing
//                                            : paramMeta.Description;
//                 
//                 context.ClarificationModeEnabled  = true;
//                 context.ClarificationForAction    = action.Name;
//                 context.ClarificationForParameter = firstMissing;
//                 
//                 var question = $"I can run {action.Name}, but I need a value for '{friendlyName}'. "
//                              + $"Whatever you type next will be used EXACTLY as the value. No interpretation.";
//
//                 context.PendingAction = new PendingAction
//                                         {
//                                                 ActionName = action.Name
//                                               , CollectedParameters = new Dictionary<string, string>(interpretation.ExtractedParameters
//                                                                                                    , StringComparer.OrdinalIgnoreCase)
//                                               , RemainingParameters = missingNames
//                                         };
//
//                 return new ConverseResponse
//                        {
//                                Message = question
//                              , Debug = interpretation.DebugInfo
//                        };
//             }
//             
//             context.ClarificationModeEnabled  = false;
//             context.ClarificationForAction    = null;
//             context.ClarificationForParameter = null;
//
//             // Action does NOT allow clarification: treat as a normal failure
//             var missingJoined = string.Join(", "
//                                           , interpretation.MissingParameters);
//             return new ConverseResponse
//                    {
//                            Message = "I'm not sure what to do next."
//                          , Debug = $"Missing required parameters for action '{interpretation.ActionName}': {missingJoined}"
//                    };
//         }
//
//         // 4e. No action chosen at all (e.g. nonsense input or other failure)
//         if (interpretation.ActionName is null)
//         {
//             return new ConverseResponse
//                    {
//                            Message = "I'm not sure what to do next."
//                          , Debug = interpretation.DebugInfo
//                    };
//         }
//
//         // 5. Look up the action reflectively
//         var selectedAction = _registry.Actions
//                                       .FirstOrDefault(metadata => string.Equals(metadata.Name
//                                                                               , interpretation.ActionName
//                                                                               , StringComparison.OrdinalIgnoreCase));
//
//         if (selectedAction is null)
//         {
//             var msg = $"Interpreter selected unknown action '{interpretation.ActionName}'.";
//             _telemetry.Track("ActionLookup.Failed"
//                            , msg);
//
//             return new ConverseResponse
//                    {
//                            Message = "That action is not registered in this system."
//                          , Debug = msg
//                    };
//         }
//
//         // 6. Execute (sync) with whatever parameters we have
//         var execResult = _execution.Execute(selectedAction
//                                           , interpretation.ExtractedParameters);
//
//         // 7. Return consolidated response
//         return new ConverseResponse
//                {
//                        Message = execResult
//                      , Debug = interpretation.DebugInfo
//                };
//     }
//     
//     private static string NormalizeUserValue(string raw)
//     {
//         if (string.IsNullOrWhiteSpace(raw))
//             return raw;
//
//         var trimmed = raw.Trim();
//
//         // Case: starts and ends with matching quotes → strip the outer quotes
//         if ((trimmed.StartsWith("\"") && trimmed.EndsWith("\"")) ||
//             (trimmed.StartsWith("'")  && trimmed.EndsWith("'")))
//         {
//             return trimmed.Substring(1, trimmed.Length - 2);
//         }
//
//         return trimmed;
//     }
//     
// }