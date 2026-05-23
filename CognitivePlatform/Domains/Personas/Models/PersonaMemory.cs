namespace CognitivePlatform.Api.Domains.Personas.Models;

public class PersonaMemory
{
    public Guid         Id                      { get; set; }
    public Guid         PersonaId               { get; set; }
    public string       Content                 { get; set; } = string.Empty;
    public MemoryType   Type                    { get; set; }
    public float        Confidence              { get; set; }
    public float        EmotionalWeight         { get; set; }
    public bool         UserAsserted            { get; set; }
    public bool         AIInferred              { get; set; }
    public List<Guid>   RelatedMemories         { get; set; } = [];
    public MemoryState  State                   { get; set; } = MemoryState.Provisional;
    public MemorySource Source                  { get; set; }
    public string?      InferenceChain          { get; set; }
    public List<Guid>   ContradictionReferences { get; set; } = [];
    public DateTime     CreatedUtc              { get; set; }
    public DateTime     LastModifiedUtc         { get; set; }
}
