namespace CognitivePlatform.Api.Conversation;

public class ConversationMetadata
{
    public string   ConversationId { get; set; } = "";
    public string?  Name           { get; set; }
    public DateTime CreatedUtc     { get; set; }
    public DateTime LastActiveUtc  { get; set; }
    public int      MessageCount   { get; set; }
    public bool     IsDeleted      { get; set; }
}
