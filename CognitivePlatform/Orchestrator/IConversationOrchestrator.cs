using CognitivePlatform.Api.Contracts;
using CognitivePlatform.Api.Controllers;
using CognitivePlatform.Api.Models;

namespace CognitivePlatform.Api.Orchestrator;

public interface IConversationOrchestrator
{
    Task<ConverseResponse> ConverseAsync (ConverseRequest   request
                                        , CancellationToken ct = default);

    IAsyncEnumerable<string> StreamAsync (ConverseRequest   request
                                        , CancellationToken ct = default);

    Task<ConverseResponse> FinalizeAsync( ConverseRequest   request
                                        , ConverseResponse  response
                                        , CancellationToken ct );
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