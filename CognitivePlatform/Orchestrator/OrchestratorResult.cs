using CognitivePlatform.Api.Models;

namespace CognitivePlatform.Api.Orchestrator;

/// <summary>
/// Simple Phase-1 result container.
/// Future phases will expand this significantly.
/// </summary>
public sealed class OrchestratorResult
{
    public string?         InterpreterDebugOutput { get; init; }
    public ActionMetadata? SelectedAction         { get; init; }
    public string?         ExecutionResult        { get; init; }
}
