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
    private readonly ITelemetrySink           _telemetry;
    private readonly ConversationContextStore _contextStore;

    public ConversationOrchestrator (IActionRegistry                                                registry
                                   , [FromKeyedServices(KeyedServices.LlmInterpreter)] IInterpreter interpreter
                                   , IExecutionEngine                                               execution
                                   , ITelemetrySink                                                 telemetry
                                   , ConversationContextStore                                       contextStore)
    {
        _registry     = registry;
        _interpreter  = interpreter;
        _execution    = execution;
        _telemetry    = telemetry;
        _contextStore = contextStore;
    }

    public async Task<ConverseResponse> ConverseAsync(ConverseRequest    request
                                                    , CancellationToken ct = default)
    {
        _telemetry.Track("Conversation.Start", $"Input='{request.Input}'");

        // 1. Get or create the session context
        var context = _contextStore.GetOrCreate(request.SessionId);

        // 2. Wire it into the test actions (needed for StoreValue, RecallValue, RepeatLastAction)
        Actions.TestActions.SetContext(context);

        // 2b. Wire the registry into meta-actions so they can introspect available actions
        Actions.MetaActions.SetRegistry(_registry);

        // 2c. Wire context into meta-actions so they can explain reasoning
        Actions.MetaActions.SetContext(context);
        
        // 3. Log interpreter identity
        _telemetry.Track("Interpreter.Selected"
                       , $"Using interpreter: {_interpreter.GetType().Name}");

        // 4. Interpret with context
        var interpretation = _interpreter.InterpretWithContext(request.Input, context);
        
        // 4b. Persist interpreter decision (success or failure) into context
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

        if (interpretation.ActionName is null)
        {
            return new ConverseResponse
                   {
                       Message = "I'm not sure what to do next."
                     , Debug   = interpretation.DebugInfo
                   };
        }

        // 5. Look up the action reflectively
        var action = _registry.Actions.FirstOrDefault(metadata => string.Equals(metadata.Name
                                                                              , interpretation.ActionName
                                                                              , StringComparison.OrdinalIgnoreCase));

        if (action is null)
        {
            var msg = $"Interpreter selected unknown action '{interpretation.ActionName}'.";
            _telemetry.Track("ActionLookup.Failed", msg);

            // Treat as failure
            context.LastFailureType       = InterpreterFailureType.NoMatchingAction;
            context.LastInterpreterReason ??=
                    "Interpreter selected an action name that is not registered.";

            return new ConverseResponse
                   {
                           Message = "That action is not registered in this system."
                         , Debug   = msg
                   };
        }
        
        // 5b. Check for missing required parameters before execution.
        var missingRequiredParameters = action.Parameters
                                              .Where(parameter => ! parameter.Optional)
                                              .Where(parameter =>
                                              {
                                                  // 1. Missing key entirely
                                                  if ( ! interpretation.ExtractedParameters
                                                                       .TryGetValue(parameter.Name
                                                                                  , out var value))
                                                      return true;

                                                  // 2. Present, but empty AND empty is not allowed
                                                  return string.IsNullOrWhiteSpace(value) && ! parameter.AllowEmpty;

                                              })
                                              .Select(p => p.Name)
                                              .ToList();

        if (missingRequiredParameters.Count > 0)
        {
            context.LastFailureType = InterpreterFailureType.MissingParameters;

            context.LastMissingParameters.Clear();
            
            foreach (var missing in missingRequiredParameters)
                context.LastMissingParameters.Add(missing);

            var debug = $"Missing required parameters for action '{interpretation.ActionName}': {string.Join(", ", missingRequiredParameters)}";

            if (string.IsNullOrWhiteSpace(context.LastInterpreterReason))
                context.LastInterpreterReason = debug;

            return new ConverseResponse
                   {
                           Message = "I'm not sure what to do next."
                         , Debug   = debug
                   };
        }

        // 6. Execute (sync)
        var execOutput = _execution.Execute(action
                                          , interpretation.ExtractedParameters);

        // 7. Return consolidated response
        return new ConverseResponse
               {
                   Message = execOutput
                 , Debug   = interpretation.DebugInfo
               };
    }
}