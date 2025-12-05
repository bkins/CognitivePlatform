using CognitivePlatform.Api.Controllers;
using CognitivePlatform.Api.Registry;
using CognitivePlatform.Api.Models;

namespace CognitivePlatform.Api.Orchestrator;

public interface IConversationOrchestrator
{
    /// <summary>
    /// Routes user input through the interpretation pipeline
    /// and returns a structured response.
    /// Phase 1 implementation uses a mock interpreter.
    /// </summary>
    OrchestratorResult Process(string input);

    Task<ConverseResponse> ConverseAsync (ConverseRequest   request
                                        , CancellationToken ct = default);
}

/// <summary>
/// Simple Phase-1 result container.
/// Future phases will expand this significantly.
/// </summary>
public class OrchestratorResult
{
    public string?         InterpreterDebugOutput { get; init; }
    public ActionMetadata? SelectedAction         { get; init; }
    public string?         ExecutionResult        { get; init; }
}