using System.Collections.Concurrent;
using CognitivePlatform.Api.Insights.Models;
using CognitivePlatform.Api.Interpreter;
using CognitivePlatform.Api.Models;

namespace CognitivePlatform.Api.Conversation;

/// <summary>
/// Immutable snapshot of the session's selected LLM provider and model.
/// Stored as a single reference field on <see cref="ConversationContext"/> and swapped
/// atomically so a reader can never observe a torn (provider, model) pair.
/// </summary>
public sealed record LlmSession(string Provider, string Model)
{
    public static LlmSession Empty { get; } = new(string.Empty, string.Empty);

    public bool HasProvider => !string.IsNullOrWhiteSpace(Provider);
    public bool HasModel    => !string.IsNullOrWhiteSpace(Model);
}

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

    public Dictionary<string, string>             LastParameters        { get; } = new(StringComparer.OrdinalIgnoreCase);
    public ConcurrentDictionary<string, string>   Metadata              { get; } = new(StringComparer.OrdinalIgnoreCase);
    public string?                                LastInterpreterReason { get; set; }

    private LlmSession _llmSession = LlmSession.Empty;

    /// <summary>
    /// The current session's LLM provider and model as an immutable snapshot.
    /// Reading this property always returns a consistent (provider, model) pair —
    /// no torn reads possible, even under concurrent <see cref="SetLlmSession(string, string)"/>
    /// or <see cref="SetLlmModel(string)"/> calls.
    /// </summary>
    public LlmSession CurrentLlmSession => Volatile.Read(ref _llmSession);

    /// <summary>
    /// Atomically replaces the session's LLM provider and model in a single reference swap.
    /// </summary>
    public void SetLlmSession(string provider, string model) =>
        Volatile.Write(ref _llmSession, new LlmSession(provider, model));

    public void SetLlmSession(LlmProvider provider, string model) =>
        SetLlmSession(provider.ToString(), model);

    /// <summary>
    /// Atomically swaps in a new session that keeps the existing provider but updates the model.
    /// Uses CAS to ensure no concurrent writer is lost.
    /// </summary>
    public void SetLlmModel(string model)
    {
        LlmSession current;
        LlmSession next;

        do
        {
            current = Volatile.Read(ref _llmSession);
            next    = current with { Model = model };
        }
        while (Interlocked.CompareExchange(ref _llmSession, next, current) != current);
    }

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

    private IReadOnlyList<EmittedInsightRef> _lastEmittedInsights = Array.Empty<EmittedInsightRef>();

    /// <summary>
    /// Insights emitted on the previous turn. Used to detect follow-through
    /// (InsightOutcome.ActedOn) when the user's next action matches a SuggestedAction.
    ///
    /// Stored as a single immutable reference and swapped atomically (BUG-14 pattern):
    /// readers always observe a consistent snapshot, even under concurrent writes.
    /// </summary>
    public IReadOnlyList<EmittedInsightRef> LastEmittedInsights => Volatile.Read(ref _lastEmittedInsights);

    /// <summary>
    /// Atomically replaces the previous-turn insights snapshot.
    /// Pass an empty list (or null) to clear.
    /// </summary>
    public void SetLastEmittedInsights(IReadOnlyList<EmittedInsightRef>? insights) =>
        Volatile.Write(ref _lastEmittedInsights, insights ?? Array.Empty<EmittedInsightRef>());

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

        Volatile.Write(ref _llmSession,         LlmSession.Empty);
        Volatile.Write(ref _lastEmittedInsights, Array.Empty<EmittedInsightRef>());
    }
}