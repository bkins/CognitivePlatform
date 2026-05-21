using CognitivePlatform.Api.Conversation;

namespace CognitivePlatform.Api.Execution;

/// <summary>
/// Action classes implement this to receive the live session context before execution.
/// ExecutionEngine sets it so side-effects like tier-downgrade notes land in the right context.
/// </summary>
public interface ISessionAware
{
    void SetSessionContext(ConversationContext context);
}
