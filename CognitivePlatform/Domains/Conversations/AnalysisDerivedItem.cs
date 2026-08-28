namespace CognitivePlatform.Api.Domains.Conversations;

public class AnalysisDerivedItem
{
    public Guid       Id                        { get; set; } = Guid.NewGuid();
    public Guid       ConversationId            { get; set; }
    public string     Type                      { get; set; } = string.Empty;
    public string     Content                   { get; set; } = string.Empty;
    public List<Guid> SourceTranscriptSegmentIds { get; set; } = new();
}
