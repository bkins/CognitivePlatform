namespace CognitivePlatform.Api.Domains.Conversations;

public class Transcript
{
    public Guid                    Id             { get; set; } = Guid.NewGuid();
    public Guid                    ConversationId { get; set; }
    public TranscriptionStatus     Status         { get; set; } = TranscriptionStatus.NotProcessed;
    public List<TranscriptSegment>  Segments       { get; set; } = new();
    public DateTime                CreatedAtUtc   { get; set; } = DateTime.UtcNow;
    public DateTime?               ProcessedAtUtc { get; set; }
    public bool                    IsDiarized     { get; set; }
    public DateTime?               DiarizedAtUtc  { get; set; }
    public string?                 ErrorMessage   { get; set; }
    public bool                    IsDeleted      { get; set; }
    public DateTime?               DeletedUtc     { get; set; }
}
