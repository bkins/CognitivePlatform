namespace CognitivePlatform.Api.Domains.Conversations;

public class TranscriptSegment
{
    public Guid     Id         { get; set; } = Guid.NewGuid();
    public TimeSpan Start      { get; set; }
    public TimeSpan End        { get; set; }
    public string   Text       { get; set; } = string.Empty;
    public string?  SpeakerId    { get; set; }
    public string   SpeakerLabel { get; set; } = "Speaker 1";
    public double?  Confidence   { get; set; }
}
