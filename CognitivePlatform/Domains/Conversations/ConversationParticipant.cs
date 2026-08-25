namespace CognitivePlatform.Api.Domains.Conversations;

public class ConversationParticipant
{
    public Guid      Id             { get; set; } = Guid.NewGuid();
    public Guid      ConversationId { get; set; }
    public string    SpeakerId      { get; set; } = string.Empty;
    public string    SpeakerLabel   { get => SpeakerId; set => SpeakerId = value ?? string.Empty; }
    public string?   DisplayName    { get; set; }
    public bool      IsDeleted      { get; set; }
    public DateTime? DeletedUtc     { get; set; }
}
