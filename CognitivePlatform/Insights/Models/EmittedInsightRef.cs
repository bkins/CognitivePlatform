namespace CognitivePlatform.Api.Insights.Models;

/// <summary>
/// Lightweight reference stored on <see cref="CognitivePlatform.Api.Conversation.ConversationContext"/>
/// after insights are emitted. Used to detect follow-through in the next turn.
/// </summary>
public sealed record EmittedInsightRef(string DeduplicationKey, string? SuggestedAction);
