namespace CognitivePlatform.Api.Domains.Personas.Models;

public class CanonicalPersona
{
    public Guid                 Id                  { get; set; }
    public string               Name                { get; set; } = string.Empty;
    public string?              ScenarioDescription { get; set; }
    public PersonaConfiguration Configuration       { get; set; } = PersonaConfiguration.Default;
    public PersonalityProfile   Personality         { get; set; } = new();
    public RelationshipModel    RelationshipState   { get; set; } = new();
    public EmotionalModel       EmotionalState      { get; set; } = new();
    public NarrativeTimeline    Timeline            { get; set; } = new();
    public List<PersonaMemory>  Memories            { get; set; } = [];
    public List<MemorySnapshot> Snapshots           { get; set; } = [];
    public CanonRules           CanonRules          { get; set; } = new();
    public PersonaMetadata      Metadata            { get; set; } = new();
    public bool                 IsDeleted           { get; set; }
    public DateTime?            DeletedUtc          { get; set; }
    public DateTime             CreatedUtc          { get; set; }
    public DateTime             LastModifiedUtc     { get; set; }
}
