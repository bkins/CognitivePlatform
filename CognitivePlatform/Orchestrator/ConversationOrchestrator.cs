using System.Runtime.CompilerServices;
using CognitivePlatform.Api.Avails;
using CognitivePlatform.Api.Avails.Extensions;
using CognitivePlatform.Api.Contracts;
using CognitivePlatform.Api.Conversation;
using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Domains.Journal.Interfaces;
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
    private readonly FastPathResolver         _fastPath;
    private readonly ILlmClient               _llmClient;
    private readonly IIdempotencyStore        _idempotencyStore;

    public ConversationOrchestrator( IActionRegistry                                                registry
                                   , [FromKeyedServices(KeyedServices.LlmInterpreter)] IInterpreter interpreter
                                   , IExecutionEngine                                               execution
                                   , ConversationContextStore                                       contextStore
                                   , ITelemetrySink                                                 telemetry
                                   , FastPathResolver                                               fastPathResolver
                                   , ILlmClient                                                     llmClient
                                   , IIdempotencyStore                                              idempotencyStore )
    {
        _registry         = registry         ?? throw new ArgumentNullException(nameof(registry));
        _interpreter      = interpreter      ?? throw new ArgumentNullException(nameof(interpreter));
        _execution        = execution        ?? throw new ArgumentNullException(nameof(execution));
        _contextStore     = contextStore     ?? throw new ArgumentNullException(nameof(contextStore));
        _telemetry        = telemetry        ?? throw new ArgumentNullException(nameof(telemetry));
        _fastPath         = fastPathResolver ?? throw new ArgumentNullException(nameof(fastPathResolver));
        _llmClient        = llmClient        ?? throw new ArgumentNullException(nameof(llmClient));
        _idempotencyStore = idempotencyStore ?? throw new ArgumentNullException(nameof(idempotencyStore));
    }


    public async Task<ConverseResponse> ConverseAsync(ConverseRequest    request
                                                    , CancellationToken ct = default)
    {
        if (request?.Input is null) throw new ArgumentNullException(nameof(request));
        
        if (request.ClientRequestId.HasValue)
        {
            var existing = await _idempotencyStore.TryGetAsync(request.ClientRequestId.Value, ct);

            if (existing is not null)
            {
                _telemetry.Track("Idempotency.Hit"
                               , request.ClientRequestId.Value.ToString());

                return existing;
            }
        }
        
        _telemetry.Track("Conversation.Start", $"Model='{request.Model}'");

        // 1. Get or create the session context
        var context = _contextStore.GetOrCreate(request.SessionId);
        
// 🔑 Wire meta-actions FIRST
        Actions.MetaActions.SetRegistry(_registry);
        Actions.MetaActions.SetContext(context);

// 🔑 Also wire test actions if they might be fast-pathed
        Actions.TestActions.SetContext(context);
        
        // if (_fastPath.TryResolve(request.Input
        //                        , out var actionMeta
        //                        , out var fastParams))
        // {
        //     _telemetry.Track("FastPath.Resolved",
        //                      $"Action={actionMeta!.Name}");
        //     
        //     // J-03: Persist JournalDraft locally (local-first capture)
        //     // Only do this for the journal "AddJournalEntry" fast-path action.
        //     // (Adjust name if your action is named differently in the registry.)
        //     
        //     //Documentation on how this will work: 
        //     //C:\Users\benho\source\Application Documentation\The CP Universe\Natural Language Command System\Flow - User Input - API - Draft - Execution.md
        //     if (actionMeta!.Name.Equals("AddJournalEntry", StringComparison.OrdinalIgnoreCase))
        //     {
        //         var parsed = _journalParser.Parse(fastParams!.FirstOrDefault().Value ?? string.Empty);
        //         
        //         // J-02 rule: only quoted directives become structured fields;
        //         // unquoted directive-like text stays in Text (handled by parser).
        //         var draft = new JournalDraft
        //                     {
        //                             Text      = parsed.Text
        //                           , Tags      = parsed.Tags
        //                           , Mood      = parsed.Mood
        //                           , State     = JournalDraftState.Local
        //                           , MoodScore = parsed.MoodScore
        //                     };
        //
        //         await _journalDraftRepository.AddAsync(draft, ct);
        //         fastParams = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        //                      {
        //                              ["text"] = parsed.Text
        //                      };
        //
        //         if (parsed.Tags.Count > 0)
        //             fastParams["tags"] = string.Join(", ", parsed.Tags);
        //
        //         if (parsed.Mood.HasValue())
        //             fastParams["mood"] = parsed.Mood;
        //
        //         if (parsed.MoodScore.HasValue)
        //             fastParams["moodScore"] = parsed.MoodScore.Value.ToString();
        //         
        //     }
        //     
        //     var result = _execution.Execute(actionMeta!, fastParams!);
        //     _telemetry.Track("ConverseAsync.FastPath", result);
        //
        //     var parameters = string.Join(", ", fastParams!.Select(pair => $"{pair.Key}={pair.Value}"));
        //     
        //     if (request.ClientRequestId.HasValue)
        //     {
        //         var existing = await _idempotencyStore.TryGetAsync(request.ClientRequestId.Value, ct);
        //         if (existing is not null)
        //         {
        //             _telemetry.Track("Idempotency.Hit", request.ClientRequestId.Value.ToString());
        //             return existing;
        //         }
        //     }
        //     
        //     return new ConverseResponse
        //            {
        //                    Message = result
        //                  , Debug = $"FastPath → Action={actionMeta!.Name}"
        //                          + $", Params=[{parameters}]"
        //            };
        // }
        
        if (_fastPath.TryResolve(request.Input, out var actionMeta, out var fastParams))
        {
            _telemetry.Track("FastPath.Resolved", $"Action={actionMeta!.Name}");

            var result = _execution.Execute(actionMeta!, fastParams!);

            var parameters = string.Join(", ", fastParams!.Select(pair => $"{pair.Key}={pair.Value}"));

            var response = new ConverseResponse
                           {
                                   Message = result,
                                   Debug   = $"FastPath → Action={actionMeta!.Name}, Params=[{parameters}]"
                           };

            return await FinalizeAsync(request, response, ct);
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

        // 2. Wire it into the test actions (needed for StoreValue, RecallValue, RepeatLastAction)
        //Actions.TestActions.SetContext(context);

        // 2b. Wire the registry into meta-actions so they can introspect available actions
        //Actions.MetaActions.SetRegistry(_registry);

        // 2c. Wire context into meta-actions so they can explain reasoning
        //Actions.MetaActions.SetContext(context);

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
                    var result     = _execution.Execute(confirmedAction, execParams);

                    var confirmationResponse = new ConverseResponse
                           {
                                   Message = result
                                 , Debug = "Delete confirmed and executed."
                           };
                    return await FinalizeAsync(request, confirmationResponse, ct);
                }

                if (IsNegative(input).Not())
                    return new ConverseResponse
                           {
                                   Message = "Please confirm or cancel the deletion."
                                 , Debug = "Awaiting delete confirmation."
                           };
                
                context.PendingAction = null;

                var response = new ConverseResponse
                               {
                                       Message = "Deletion cancelled."
                                     , Debug = "Delete cancelled by user."
                               };
                return await FinalizeAsync(request
                                         , response
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
                _telemetry.Track("Clarification.ActionMissing", message);

                var response = new ConverseResponse
                       {
                               Message = message
                             , Debug   = "Pending action not found in registry."
                       };
                return await FinalizeAsync(request, response, ct);
            }

            // If somehow no remaining parameters, just execute with what we have
            if (pending.RemainingParameters.Count == 0)
            {
                context.PendingAction = null;

                var finalParameters = ApplyDefaultValues(action, pending.CollectedParameters);
                var execOutput      = _execution.Execute(action, finalParameters);

                _telemetry.Track("Clarification.Completed"
                               , $"Action={pending.ActionName}, "
                               + $"Collected={pending.CollectedParameters.Count}");

                var response = new ConverseResponse
                       {
                               Message = execOutput
                             , Debug   = $"Executed pending action '{pending.ActionName}' with previously collected parameters."
                       };
                return await FinalizeAsync(request, response, ct);
            }


            // Take the next missing parameter name
            var nextParameterName = pending.RemainingParameters[0];

            // Whatever the user typed on this turn becomes the value for that parameter.
            var userValue = request.Input ?? string.Empty;

            pending.CollectedParameters[nextParameterName] = userValue;
            pending.RemainingParameters.RemoveAt(0);

            _telemetry.Track("Clarification.ParameterCollected"
                           , $"Action={pending.ActionName}, "
                           + $"Parameter={nextParameterName}, "
                           + $"Value='{userValue}'");

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
                             , Debug   = $"Clarification: collected '{nextParameterName}' = '{userValue}'. "
                                       + $"Still need parameter '{friendlyName}'."
                       };
                
                return await FinalizeAsync(request, response, ct);
            }

            // execute the action now
            context.PendingAction = null;

            var parameters  = ApplyDefaultValues(action, pending.CollectedParameters);
            var finalOutput = _execution.Execute(action, parameters);

            _telemetry.Track("Clarification.Completed",
                             $"Action={pending.ActionName}, "
                           + $"Collected={pending.CollectedParameters.Count}");

            var finalClarificationResponse = new ConverseResponse
                   {
                           Message = finalOutput,
                           Debug   = $"Executed pending action '{pending.ActionName}' "
                                   + $"after collecting all required parameters."
                   };
            return await FinalizeAsync(request, finalClarificationResponse, ct);
        }

        // 4. Log interpreter identity
        _telemetry.Track("ConversationOrchestrator.Interpreter.Selected"
                       , $"Using interpreter: {_interpreter.GetType().Name}");
        // TODO: Define `WasResolvedFor` first
        // Debug.Assert(!_fastPath.WasResolvedFor(request),
        //              "Interpreter should not run after FastPath resolution.");

        // 5. Interpret with context
            var interpretation = await _interpreter.InterpretWithContext(request.Input
                                                                       , context);
        

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
                _telemetry.Track("ActionLookup.Failed", msg);

                var response = new ConverseResponse
                       {
                               Message = "That action is not registered in this system."
                             , Debug   = msg
                       };
                return await  FinalizeAsync(request, response, ct);
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
                       };
                return await  FinalizeAsync(request, response, ct);
            }

            // Action does NOT allow clarification: treat as a normal failure
            var missingJoined = string.Join(", ", interpretation.MissingParameters);
            var missingParametersResponse = new ConverseResponse
                   {
                           Message = "I'm not sure what to do next."
                         , Debug   = $"Missing required parameters for action '{interpretation.ActionName}': {missingJoined}"
                   };
            return await FinalizeAsync(request, missingParametersResponse, ct);
        }
        
        // 7. No action chosen at all (e.g. nonsense input or other failure)
        if (string.IsNullOrWhiteSpace(interpretation.ActionName))
        {
            var missingActionResponse = new ConverseResponse
                   {
                           Message = "I'm not sure what to do next."
                         , Debug   = interpretation.DebugInfo
                   };
            return await FinalizeAsync(request, missingActionResponse, ct);
        }

        // 8. Look up the action reflectively
        var selectedAction = _registry.Actions.FirstOrDefault(metadata => string.Equals(metadata.Name
                                                                                      , interpretation.ActionName
                                                                                      , StringComparison.OrdinalIgnoreCase));

        if (selectedAction is null)
        {
            var msg = $"Interpreter selected unknown action '{interpretation.ActionName}'.";
            _telemetry.Track("ActionLookup.Failed", msg);

            var unknownActionResponse = new ConverseResponse
                                        {
                                                Message = "That action is not registered in this system."
                                              , Debug   = msg
                                        };
            return await FinalizeAsync(request, unknownActionResponse, ct);

        }

        if (selectedAction.Name == "DeleteJournalEntry"
         && context.HasConfirmedDelete
                   .Not())
        {
            var parameters = interpretation.ExtractedParameters;

            // Build a human-readable review prompt
            var reason = parameters.TryGetValue("reason", out var value)
                                 ? value
                                 : "(no reason provided)";

            var reviewMessage = "Delete journal entry?\n\n"
                              + $"Reason: {reason}\n\n"
                              + "This entry will be marked as deleted and hidden from your journal, "
                              + "but its history will be preserved.\n\n"
                              + "Please confirm or cancel.";

            context.PendingAction = new PendingAction
                                    {
                                            ActionName = selectedAction.Name
                                          , CollectedParameters = new Dictionary<string, string>(parameters
                                                                                               , StringComparer.OrdinalIgnoreCase)
                                          , RemainingParameters  = new List<string>()
                                          , ConfirmationRequired = true
                                          , ConfirmationPrompt   = reviewMessage
                                    };

            var response = new ConverseResponse
                           {
                                   Message = reviewMessage
                                 , Debug = "Delete action requires confirmation."
                           };
            return await FinalizeAsync(request, response, ct);
        }
        
        // 9. Execute (sync) with whatever parameters we have (including defaults for optionals)
        var execParameters  = ApplyDefaultValues(selectedAction, interpretation.ExtractedParameters);
        var execOutputFinal = _execution.Execute(selectedAction, execParameters);

        // 10. Return a consolidated response after finalizing it
        var finalResponse = new ConverseResponse
                       {
                               Message = execOutputFinal
                             , Debug   = interpretation.DebugInfo
                       };

        return await FinalizeAsync(request, finalResponse, ct);
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

        // ✅ J-01.1: FastPath always wins.
        // In streaming mode: if FastPath resolves, execute and emit a single chunk.
        // If it doesn't resolve, fall back to normal streaming/interpreter behavior.
        if (_fastPath.TryResolve(request.Input, out var actionMeta, out var fastParams))
        {
            _telemetry.Track("FastPath.Resolved.Stream",
                             $"Action={actionMeta!.Name}");

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
            
            var result = _execution.Execute(actionMeta!, fastParams!);
            _telemetry.Track("ConverseAsync.FastPath.Stream", result);

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
        await foreach (var chunk in _llmClient.StreamAsync(
                           request.Input,
                           request.Model,
                           ct))
        {
            yield return chunk;
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

    public async Task<ConverseResponse> FinalizeAsync( ConverseRequest   request
                                                      , ConverseResponse  response
                                                      , CancellationToken ct )
    {
        if (request.ClientRequestId.HasValue)
        {
            await _idempotencyStore.StoreAsync(request.ClientRequestId.Value
                                             , response
                                             , ct);
        }

        return response;
    }
}