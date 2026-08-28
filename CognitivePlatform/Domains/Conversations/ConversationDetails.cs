namespace CognitivePlatform.Api.Domains.Conversations;

public class ConversationDetails
{
    public required ConversationRecord           Record       { get; set; }
    public          Transcript?                   Transcript   { get; set; }
    public          List<ConversationParticipant> Participants { get; set; } = new();
    public          ConversationAnalysis?         Analysis     { get; set; }
}
