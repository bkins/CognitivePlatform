namespace CognitivePlatform.Api.Conversation;

public class PersistedConversationTurn
{
    public string         ConversationId   { get; set; } = string.Empty;
    public string         UserMessage      { get; set; } = string.Empty;
    public string         AssistantMessage { get; set; } = string.Empty;
    public DateTimeOffset OccurredAt       { get; set; }
    public TurnPath       Path             { get; set; }
    public string?        ActionName       { get; set; }
    public bool?          Succeeded        { get; set; }
}
