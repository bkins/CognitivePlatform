namespace CognitivePlatform.Api.Domains.Personas.Models;

public class EmotionalTopologyPoint
{
    public Guid     Id             { get; set; }
    public Guid     PersonaId      { get; set; }
    public string   ConversationId { get; set; } = string.Empty;
    public DateTime SampledUtc     { get; set; }
    public float    WarmthScore    { get; set; }
    public float    LongingScore   { get; set; }
    public float    GriefScore     { get; set; }
    public float    JoyScore       { get; set; }
    public string   Notes          { get; set; } = string.Empty;
}
