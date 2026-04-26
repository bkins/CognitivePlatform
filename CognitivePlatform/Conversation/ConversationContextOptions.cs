namespace CognitivePlatform.Api.Conversation;

/// <summary>
/// Bounded-history caps for <see cref="ConversationContext.Turns"/>.
/// Bound from the <c>ConversationContext</c> configuration section so both
/// caps tune without a rebuild.
///
/// <para>
/// Phase A scope: in-memory, session-scoped only. Cross-session persistence
/// of turn history is out of scope (its own Object-Store-backed ticket if
/// ever wanted).
/// </para>
/// </summary>
public sealed class ConversationContextOptions
{
    /// <summary>
    /// Maximum number of turns retained in <see cref="ConversationContext.Turns"/>.
    /// When recording a new turn would push the count above this cap, the oldest
    /// entry is evicted (FIFO sliding window).
    /// </summary>
    public int MaxTurnHistory { get; set; } = 50;

    /// <summary>
    /// Per-message length cap (UserMessage and AssistantMessage). When a message
    /// exceeds this on write, the first <see cref="MaxTurnMessageLength"/>
    /// characters are kept and the truncation marker is appended.
    /// </summary>
    public int MaxTurnMessageLength { get; set; } = 4096;

    /// <summary>
    /// Marker appended to messages truncated by <see cref="MaxTurnMessageLength"/>.
    /// </summary>
    public const string TruncationMarker = "...[truncated for context]";
}
