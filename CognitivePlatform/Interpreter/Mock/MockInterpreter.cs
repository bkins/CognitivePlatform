using CognitivePlatform.Api.Conversation;
using CognitivePlatform.Api.Registry.Capabilities;
using CognitivePlatform.Api.Telemetry;

namespace CognitivePlatform.Api.Interpreter;

public class MockInterpreter : IInterpreter
{
    private readonly ICapabilityRegistry _registry;
    private readonly ITelemetrySink      _telemetry;

    public MockInterpreter(ICapabilityRegistry registry
                         , ITelemetrySink      telemetry)
    {
        _registry  = registry;
        _telemetry = telemetry;
    }

    public async Task<InterpreterResult> Interpret(string input)
    {
        _telemetry.Track($"Interpreter.Start; Input='{input}'");

        var trimmed = input.Trim();

        var match = _registry.GetAll()
                             .FirstOrDefault(metadata => metadata.Name
                                 .StartsWithIgnoreCase(trimmed));

        var debugInfo = match is null
                            ? $"No action matched '{trimmed}'."
                            : $"Matched action '{match.Name}' using prefix rule.";

        _telemetry.Track($"Action.Match: {debugInfo}");

        _telemetry.Track($"Interpreter.End {debugInfo}");

        return new InterpreterResult
               {
                   ActionName          = match?.Name
                 , DebugInfo           = debugInfo
                 , ExtractedParameters = new Dictionary<string, string>()
               };
    }

    public async Task<InterpreterResult> InterpretWithContext( string              input
                                                             , ConversationContext context
                                                             , TaskComplexity      complexity = TaskComplexity.Standard )
    {
        var ctxSummary = $"[LastAction={context.LastActionName ?? "<none>"}; "
                       + $"LastUser={context.LastUserMessage ?? "<none>"}; "
                       + $"ParamCount={context.LastParameters.Count}]";

        _telemetry.Track($"Interpreter.Context; {ctxSummary}");

        return await Interpret(input);
    }
}
