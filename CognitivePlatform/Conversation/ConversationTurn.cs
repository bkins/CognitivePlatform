namespace CognitivePlatform.Api.Conversation;

/// <summary>
/// One recorded round-trip in a conversation: the user's input plus what the
/// assistant actually said back (the woven version when insights fired, the
/// raw execution result otherwise).
///
/// <para>
/// Per ENH-08 decision: turn = round-trip. Clarification and confirmation
/// chains each produce their own entries and are tagged via <see cref="Path"/>.
/// Action-parameters, token counts, execution-result body, turn IDs, and
/// per-turn insight summaries are intentionally <em>not</em> on this record —
/// each was considered and rejected to keep the snapshot lean.
/// </para>
///
/// <para>
/// Stored on <see cref="ConversationContext.Turns"/> via the BUG-14 immutable-
/// snapshot pattern; readers always see a consistent reference.
/// </para>
/// </summary>
public sealed record ConversationTurn(
        string         UserMessage
      , string         AssistantMessage
      , DateTimeOffset OccurredAt
      , TurnPath       Path
      , string?        ActionName = null
      , bool?          Succeeded  = null);
