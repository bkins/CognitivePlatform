using CognitivePlatform.Api.Conversation;
using CognitivePlatform.Api.Registry;
using CognitivePlatform.Api.Telemetry;

namespace CognitivePlatform.Api.Interpreter;

public class MockInterpreter : IInterpreter
{
    private readonly IActionRegistry _registry;
    private readonly ITelemetrySink  _telemetry;

    public MockInterpreter(IActionRegistry registry
                         , ITelemetrySink  telemetry)
    {
        _registry  = registry;
        _telemetry = telemetry;
    }

    public InterpreterResult Interpret(string input)
    {
        _telemetry.Track("Interpreter.Start", $"Input='{input}'");

        var trimmed = input.Trim();

        // Find an action whose name starts with the input (simple heuristic)
        var match = _registry.Actions
                             .FirstOrDefault(metadata => metadata.Name
                                 .StartsWith(trimmed, StringComparison.OrdinalIgnoreCase));

        var debugInfo = match is null
                            ? $"No action matched '{trimmed}'."
                            : $"Matched action '{match.Name}' using prefix rule.";

        _telemetry.Track("Interpreter.End", debugInfo);

        return new InterpreterResult
               {
                   ActionName          = match?.Name,
                   DebugInfo           = debugInfo,
                   ExtractedParameters = new Dictionary<string, string>()
               };
    }

    public InterpreterResult InterpretWithContext(string input, ConversationContext context)
    {
        // Light-weight context logging so you can see it in telemetry
        var ctxSummary = $"[LastAction={context.LastActionName ?? "<none>"}; " +
                         $"LastUser={context.LastUserMessage ?? "<none>"}; " +
                         $"ParamCount={context.LastParameters.Count}]";

        _telemetry.Track("Interpreter.Context", ctxSummary);

        // Reuse the same simple rule-based behavior for now
        return Interpret(input);
    }
}
