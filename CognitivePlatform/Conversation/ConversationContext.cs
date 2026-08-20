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

    public bool HasProvider => Provider.HasValue();
    public bool HasModel    => Model.HasValue();
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

    private readonly ConversationContextOptions _options;

    private IReadOnlyList<ConversationTurn> _turns = Array.Empty<ConversationTurn>();

    /// <summary>
    /// Bounded, in-memory, session-scoped history of recent turns. Stored as a
    /// single immutable reference and swapped atomically (BUG-14 pattern) — readers
    /// always observe a consistent snapshot, even while the orchestrator records
    /// or replaces the latest turn.
    ///
    /// <para>
    /// The window is sized by <see cref="ConversationContextOptions.MaxTurnHistory"/>
    /// (default 50). Per-message length is capped by
    /// <see cref="ConversationContextOptions.MaxTurnMessageLength"/> (default 4096).
    /// </para>
    /// </summary>
    public IReadOnlyList<ConversationTurn> Turns => Volatile.Read(ref _turns);

    /// <summary>
    /// Appends <paramref name="turn"/> to the session's turn history, evicting
    /// the oldest entry when the cap would be exceeded. Both message fields are
    /// truncated at <see cref="ConversationContextOptions.MaxTurnMessageLength"/>.
    ///
    /// <para>
    /// Single-writer contract: the orchestrator is the sole caller and processes
    /// turns serially per session. A plain Volatile.Read-build-Volatile.Write
    /// sequence is sufficient; no CAS loop is required. Concurrent <em>readers</em>
    /// (e.g. providers) still see consistent snapshots via <see cref="Turns"/>.
    /// </para>
    /// </summary>
    public void RecordTurn(ConversationTurn turn)
    {
        ArgumentNullException.ThrowIfNull(turn);

        var truncated = TruncateMessages(turn);

        var current = Volatile.Read(ref _turns);
        var next    = new List<ConversationTurn>(Math.Min(current.Count + 1, _options.MaxTurnHistory));

        // Slide the window: drop the oldest entry once we'd exceed the cap.
        var skip = current.Count + 1 > _options.MaxTurnHistory ? current.Count + 1 - _options.MaxTurnHistory : 0;
        for (var i = skip; i < current.Count; i++)
            next.Add(current[i]);

        next.Add(truncated);

        Volatile.Write(ref _turns, next);
    }

    /// <summary>
    /// Replaces the most recent recorded turn with <paramref name="turn"/>. Used
    /// after a weave pass to swap the un-woven assistant message for the woven one.
    ///
    /// <para>
    /// Same single-writer contract as <see cref="RecordTurn"/>.
    /// </para>
    ///
    /// <para>
    /// Throws <see cref="InvalidOperationException"/> if there is no prior turn —
    /// calling <c>ReplaceLatestTurn</c> without a prior <see cref="RecordTurn"/> is
    /// a contract violation in the orchestrator's flow.
    /// </para>
    /// </summary>
    public void ReplaceLatestTurn(ConversationTurn turn)
    {
        ArgumentNullException.ThrowIfNull(turn);

        var current = Volatile.Read(ref _turns);
        if (current.Count == 0)
            throw new InvalidOperationException("Cannot replace latest turn: history is empty.");

        var truncated = TruncateMessages(turn);

        var next = new List<ConversationTurn>(current.Count);
        for (var i = 0; i < current.Count - 1; i++)
            next.Add(current[i]);

        next.Add(truncated);

        Volatile.Write(ref _turns, next);
    }

    private ConversationTurn TruncateMessages(ConversationTurn turn) =>
        turn with
        {
                UserMessage      = TruncateOne(turn.UserMessage)
              , AssistantMessage = TruncateOne(turn.AssistantMessage)
        };

    private string TruncateOne(string message)
    {
        if (message.Length <= _options.MaxTurnMessageLength)
            return message;

        return string.Concat(message.AsSpan(0, _options.MaxTurnMessageLength)
                           , ConversationContextOptions.TruncationMarker);
    }

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
        : this(sessionId, options: null)
    {
    }

    public ConversationContext (string sessionId, ConversationContextOptions? options)
    {
        SessionId = sessionId;
        _options  = options ?? new ConversationContextOptions();
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
        Volatile.Write(ref _turns,              Array.Empty<ConversationTurn>());
    }
}