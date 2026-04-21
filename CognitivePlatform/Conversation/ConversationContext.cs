using CognitivePlatform.Api.Insights.Models;
using CognitivePlatform.Api.Models;

namespace CognitivePlatform.Api.Conversation;

/// <summary>
/// Per-session state for a conversation with the CognitivePlatform.
/// This is the "source of truth" for everything the backend knows about
/// the current session (last action, interpreter, raw model reply, etc.).
/// </summary>
public class ConversationContext
{
    public string? LastUserMessage { get; set; }

    /// <summary>
    /// The last successfully executed action name (e.g. "StoreValue", "RecallValue").
    /// </summary>
    public string? LastActionName { get; set; }

    /// <summary>
    /// The concrete interpreter type that handled the last turn
    /// (e.g. "LlmInterpreter").
    /// </summary>
    public string? LastInterpreterName { get; set; }

    /// <summary>
    /// Raw model reply as a string, exactly as returned by the LLM
    /// before any JSON parsing.
    /// </summary>
    public string? LastInterpreterRawReply { get; set; }

    public Dictionary<string, string> LastParameters        { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> Metadata              { get; } = new(StringComparer.OrdinalIgnoreCase);
    public string?                    LastInterpreterReason { get; set; }

    public Exception? LastInterpreterException { get; set; } = null;
    
    /// <summary>
    /// Optional: a short debug summary captured from the interpreter
    /// (e.g. "LlmInterpreter completed. UserInput: ... ModelActionName: ...").
    /// This is perfect to surface as interpreterDebugOutput in the
    /// Phase 2 Smoke Tester.
    /// </summary>
    public string? LastInterpreterDebug { get; set; }

    /// <summary>
    /// Optional: structured debug JSON from the interpreter.
    /// For example, you can store the parsed JSON with actionName,
    /// parameters, failureType, candidateActions, missingParameters, etc.
    ///
    /// You don't have to use this right away, but it's a nice hook
    /// for richer diagnostics in later phases.
    /// </summary>
    public string? LastInterpreterJson { get; set; }

    public InterpreterFailureType LastFailureType       { get; set; } = InterpreterFailureType.None;
    public List<string>           LastCandidateActions  { get; }      = new();
    public List<string>           LastMissingParameters { get; }      = new();

    /// <summary>
    /// Stable identifier for this conversation session.
    /// The Phase 2 Smoke Tester passes this in as the "Using session: ..." value.
    /// </summary>
    public string SessionId { get; }

    /// <summary>
    /// If non-null, indicates we asked the user a follow-up question
    /// to complete this action.
    /// </summary>
    public PendingAction? PendingAction { get; set; }

    public bool    ClarificationModeEnabled  { get; set; }
    public string? ClarificationForAction    { get; set; }
    public string? ClarificationForParameter { get; set; }

    /// <summary>
    /// Insights emitted on the previous turn. Used to detect follow-through
    /// (InsightOutcome.ActedOn) when the user's next action matches a SuggestedAction.
    /// </summary>
    public IReadOnlyList<EmittedInsightRef> LastEmittedInsights { get; set; } = [];

    public ConversationContext (string sessionId)
    {
        SessionId = sessionId;
    }

    public void Reset()
    {
        LastUserMessage       = null;
        LastActionName        = null;
        LastInterpreterReason = null;
        LastInterpreterDebug  = null;
        PendingAction         = null;

        LastFailureType = InterpreterFailureType.None;

        LastParameters.Clear();
        Metadata.Clear();
        LastCandidateActions.Clear();
        LastMissingParameters.Clear();
    }
}