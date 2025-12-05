using CognitivePlatform.Api.Avails;
using CognitivePlatform.Api.Controllers;
using CognitivePlatform.Api.Conversation;
using CognitivePlatform.Api.Execution;
using CognitivePlatform.Api.Interpreter;
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

    public ConversationOrchestrator(
        IActionRegistry                                                registry,
        [FromKeyedServices(KeyedServices.LlmInterpreter)] IInterpreter interpreter,
        IExecutionEngine                                               execution,
        ITelemetrySink                                                 telemetry,
        ConversationContextStore                                       contextStore)
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
        CognitivePlatform.Api.Actions.TestActions.SetContext(context);

        // 3. Log interpreter identity
        _telemetry.Track("Interpreter.Selected",
                         $"Using interpreter: {_interpreter.GetType().Name}");

        // 4. Interpret with context
        var interp = _interpreter.InterpretWithContext(request.Input, context);

        if (interp.ActionName is null)
        {
            return new ConverseResponse
                   {
                       Message = "I'm not sure what to do next.",
                       Debug   = interp.DebugInfo
                   };
        }

        // 5. Look up the action reflectively
        var action = _registry.Actions.FirstOrDefault(a =>
                                                          string.Equals(a.Name, interp.ActionName, StringComparison.OrdinalIgnoreCase));

        if (action is null)
        {
            var msg = $"Interpreter selected unknown action '{interp.ActionName}'.";
            _telemetry.Track("ActionLookup.Failed", msg);

            return new ConverseResponse
                   {
                       Message = "That action is not registered in this system.",
                       Debug   = msg
                   };
        }

        // 6. Execute (sync)
        var interpretation = _interpreter.InterpretWithContext(request.Input, context);
        var execOutput     = _execution.Execute(action, interpretation.ExtractedParameters);

        // 7. Update context AFTER execution
        context.LastUserMessage = request.Input;
        context.LastActionName  = interp.ActionName;
        context.LastParameters.Clear();
        
        foreach (var p in interp.ExtractedParameters)
            context.LastParameters[p.Key] = p.Value;

        // 8. Return consolidated response
        return new ConverseResponse
               {
                   Message = execOutput,
                   Debug   = interp.DebugInfo
               };
    }

    // Old Phase-1 method — not used anymore
    OrchestratorResult IConversationOrchestrator.Process(string input)
        => throw new NotSupportedException(
            "Use ConverseAsync() with a session-based request.");
}