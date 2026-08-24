namespace CognitivePlatform.Api.Domains.Conversations;

public class ConversationRecord
{
    public Guid                Id             { get; set; } = Guid.NewGuid();
    public string              Title          { get; set; } = "Untitled Conversation";
    public string              AudioFilePath  { get; set; } = string.Empty;
    public string              MimeType       { get; set; } = "audio/wav";
    public TimeSpan            Duration       { get; set; }
    public long                FileSizeBytes  { get; set; }
    public TranscriptionStatus Status         { get; set; } = TranscriptionStatus.NotProcessed;
    public DateTime            RecordedAtUtc  { get; set; } = DateTime.UtcNow;
    public bool                IsDeleted      { get; set; }
    public DateTime?           DeletedUtc     { get; set; }
}
