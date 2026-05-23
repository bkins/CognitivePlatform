namespace CognitivePlatform.Api.Domains.Personas.Models;

public class EmotionalModel
{
    public string DominantEmotion { get; set; } = string.Empty;
    public string EmotionalTone   { get; set; } = string.Empty;
    public float  AttachmentLevel { get; set; }
}
