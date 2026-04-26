namespace CognitivePlatform.Api.Conversation;

/// <summary>
/// Which orchestrator branch produced a recorded turn. Stamped on every
/// <see cref="ConversationTurn"/> so providers and analytics can group or
/// filter by code-path.
/// </summary>
public enum TurnPath
{
    /// <summary>Resolved by <c>FastPathResolver</c> with no LLM round-trip.</summary>
    FastPath
    /// <summary>Routed through <c>LlmInterpreter</c> for action selection.</summary>
  , Interpreter
    /// <summary>Part of a multi-turn parameter-collection flow.</summary>
  , Clarification
    /// <summary>Part of a destructive-action confirmation gate.</summary>
  , Confirmation
}
