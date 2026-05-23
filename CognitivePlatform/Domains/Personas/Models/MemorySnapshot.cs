namespace CognitivePlatform.Api.Domains.Personas.Models;

public class MemorySnapshot
{
    public Guid                 Id               { get; set; }
    public Guid                 PersonaId        { get; set; }
    public string               Name             { get; set; } = string.Empty;
    public PersonaConfiguration Configuration    { get; set; } = PersonaConfiguration.Default;
    public List<Guid>           IncludedMemories { get; set; } = [];
    public DateTime             CreatedUtc       { get; set; }
    public string               Notes            { get; set; } = string.Empty;
}
