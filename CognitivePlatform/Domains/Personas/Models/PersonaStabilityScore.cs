namespace CognitivePlatform.Api.Domains.Personas.Models;

public class PersonaStabilityScore
{
    public Guid     PersonaId              { get; set; }
    public string   ConversationId         { get; set; } = string.Empty;
    public int      RefusalCount           { get; set; }
    public int      ImmersionBreakCount    { get; set; }
    public int      EmotionalDriftCount    { get; set; }
    public int      PolicyInterferenceCount { get; set; }
    public float    Score                  { get; set; } = 1.0f;
    public DateTime LastUpdatedUtc         { get; set; } = DateTime.UtcNow;
}
