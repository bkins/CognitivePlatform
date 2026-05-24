namespace CognitivePlatform.Api.Domains.Personas.Models;

/// <summary>
/// Describes a detected conflict between a candidate memory and an existing
/// canonical or reinforced memory for the same persona.
/// </summary>
public class MemoryContradiction
{
    public Guid   ExistingMemoryId   { get; set; }
    public Guid   CandidateMemoryId  { get; set; }
    public string Reason             { get; set; } = string.Empty;
    public float  ConflictScore      { get; set; }
}
