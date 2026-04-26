using System.Diagnostics;
using CognitivePlatform.Api.Contracts;
using CognitivePlatform.Api.Controllers;
using CognitivePlatform.Api.Conversation;
using CognitivePlatform.Api.Models;

namespace CognitivePlatform.Api.Orchestrator;

public interface IConversationOrchestrator
{
    Task<ConverseResponse> ConverseAsync (ConverseRequest   request
                                        , CancellationToken ct = default);

    IAsyncEnumerable<string> StreamAsync (ConverseRequest   request
                                        , CancellationToken ct = default);

    /// <summary>
    /// Records the turn, persists idempotency state, and emits completion telemetry.
    /// The <paramref name="path"/> is required so the recorded turn carries the right
    /// <see cref="TurnPath"/>; <paramref name="recordTurn"/> defaults to true so most
    /// branches don't need to think about it. The Interpreter+execute path passes
    /// <c>recordTurn: false</c> because it records inline around the engine call.
    /// </summary>
    Task<ConverseResponse> FinalizeAsync( ConverseRequest   request
                                        , ConverseResponse  response
                                        , Stopwatch         sw
                                        , TurnPath          path
                                        , string?           actionName = null
                                        , bool?             succeeded  = null
                                        , bool              recordTurn = true
                                        , CancellationToken ct         = default );
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