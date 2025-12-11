using System.Collections.Concurrent;

namespace CognitivePlatform.Api.Conversation;

public class ConversationContextStore
{
    private readonly ConcurrentDictionary<string, ConversationContext> _sessions = new();

    public ConversationContext GetOrCreate(string sessionId) => _sessions.GetOrAdd(sessionId, _ => new ConversationContext(sessionId));
}