using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace CognitivePlatform.Api.Conversation;

public class ConversationContextStore
{
    private readonly ConcurrentDictionary<string, ConversationContext> _sessions = new();
    private readonly ConversationContextOptions                        _options;

    /// <summary>Default ctor preserved for tests / call-sites that don't need bounded options.</summary>
    public ConversationContextStore()
        : this(new OptionsWrapper<ConversationContextOptions>(new ConversationContextOptions()))
    {
    }

    public ConversationContextStore(IOptions<ConversationContextOptions> options)
    {
        _options = options?.Value ?? new ConversationContextOptions();
    }

    public ConversationContext GetOrCreate(string sessionId) =>
        _sessions.GetOrAdd(sessionId
                         , id => new ConversationContext(id, _options));
}
