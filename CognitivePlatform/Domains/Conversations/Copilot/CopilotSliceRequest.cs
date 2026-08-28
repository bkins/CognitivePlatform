namespace CognitivePlatform.Api.Domains.Conversations.Copilot;

public class CopilotSliceRequest
{
    public int     SliceIndex        { get; set; }
    public double  OffsetSeconds     { get; set; }
    public double  DurationSeconds   { get; set; }
    public string? ContextWindowText { get; set; }
    public string  MimeType          { get; set; } = "audio/wav";
}
