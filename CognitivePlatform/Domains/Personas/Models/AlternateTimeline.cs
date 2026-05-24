namespace CognitivePlatform.Api.Domains.Personas.Models;

public class AlternateTimeline
{
    public Guid                  Id               { get; set; }
    public Guid                  PersonaId        { get; set; }
    public string                Name             { get; set; } = string.Empty;
    public string                Premise          { get; set; } = string.Empty;
    public DateTime              BranchPointUtc   { get; set; }
    public List<PersonaMemory>   TimelineMemories { get; set; } = [];
    public List<TimelineEvent>   Events           { get; set; } = [];
    public DateTime              CreatedUtc       { get; set; }
    public bool                  IsDeleted        { get; set; }
    public DateTime?             DeletedUtc       { get; set; }
}
