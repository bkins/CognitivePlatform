using CognitivePlatform.Api.Conversation;
using CognitivePlatform.Api.Models;
using CognitivePlatform.Api.Telemetry.Events;

namespace CognitivePlatform.Api.Interpreter;

public interface IInterpreter
{
    Task<InterpreterResult> InterpretWithContext (string              input
                                                , ConversationContext context
                                                , TaskComplexity      complexity = TaskComplexity.Standard);
}

/// <summary>
/// The output of the interpreter in Phase 1.
/// Future phases will expand this with confidence scores, missing params,
/// clarifying questions, and multi-model results.
/// </summary>
public class InterpreterResult
{
    /// <summary>
    /// The method name the interpreter believes the user is referring to.
    /// Phase 1: purely matched by name or simple keyword rule.
    /// </summary>
    public string? ActionName { get; init; }

    /// <summary>
    /// Debug information useful for inspecting interpreter behavior.
    /// </summary>
    public string DebugInfo { get; init; } = string.Empty;

    /// <summary>
    /// Phase 1 does not support parameter extraction, so this remains empty.
    /// Future phases will populate this dictionary.
    /// </summary>
    public Dictionary<string, string> ExtractedParameters { get; init; } = new();

    /// <summary>
    /// Short explanation from the model of why it chose (or failed to choose) an action.
    /// </summary>
    public string? Reason { get; init; }
    
    /// <summary>
    /// Interpreter-level failure classification.
    /// </summary>
    public InterpreterFailureType     FailureType         { get; init; } = InterpreterFailureType.None;

    /// <summary>
    /// Exception details if the interpreter encountered an error during processing.
    /// </summary>
    public Exception ? Exception { get; init; } = null;
    
    /// <summary>
    /// Candidate actions, if the interpreter considered multiple possibilities.
    /// </summary>
    public IReadOnlyList<string>?     CandidateActions    { get; init; }

    /// <summary>
    /// Missing parameter names, if the interpreter believes an action matched
    /// but could not extract all required parameters.
    /// </summary>
    public IReadOnlyList<string>?     MissingParameters   { get; init; }

    /// <summary>
    /// The raw JSON string returned by the LLM before parsing.
    /// Captured for training corpus use (ENH-23 Phase 3).
    /// </summary>
    public string ModelOutput { get; init; } = string.Empty;

    public LlmInterpreterCompletedEvent ToEvent()
    {
        return new LlmInterpreterCompletedEvent
               {
                       Output = $"ActionName='{ActionName}'\n\tReason='{Reason}'\n\tFailureType={FailureType}\n\tDebugInfo='{DebugInfo}'\n"
               };
    }
}
