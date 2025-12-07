using CognitivePlatform.Api.Conversation;
using CognitivePlatform.Api.Models;

namespace CognitivePlatform.Api.Interpreter;

public interface IInterpreter
{
    InterpreterResult InterpretWithContext (string              input
                                          , ConversationContext context);
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
    /// Candidate actions, if the interpreter considered multiple possibilities.
    /// </summary>
    public IReadOnlyList<string>?     CandidateActions    { get; init; }

    /// <summary>
    /// Missing parameter names, if the interpreter believes an action matched
    /// but could not extract all required parameters.
    /// </summary>
    public IReadOnlyList<string>?     MissingParameters   { get; init; }
}