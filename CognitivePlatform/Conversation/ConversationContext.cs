namespace CognitivePlatform.Api.Conversation;

public class ConversationContext
{
    public string?                    LastUserMessage       { get; set; }
    public string?                    LastActionName        { get; set; }
    public Dictionary<string, string> LastParameters        { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> Metadata              { get; } = new(StringComparer.OrdinalIgnoreCase);
    public string?                    LastInterpreterReason { get; set; }
    public string?                    LastInterpreterDebug  { get; set; }

    public void Reset()
    {
        LastUserMessage = null;
        LastActionName  = null;

        LastParameters.Clear();
        Metadata.Clear();
    }
}