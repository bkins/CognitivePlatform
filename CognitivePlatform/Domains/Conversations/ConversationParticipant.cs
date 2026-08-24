namespace CognitivePlatform.Api.Domains.Conversations;

public class ConversationParticipant
{
    public Guid    Id             { get; set; } = Guid.NewGuid();
    public Guid    ConversationId { get; set; }
    public string  SpeakerId      { get; set; } = string.Empty;
    public string? DisplayName    { get; set; }
}
