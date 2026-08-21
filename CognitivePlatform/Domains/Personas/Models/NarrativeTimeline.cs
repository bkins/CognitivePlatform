namespace CognitivePlatform.Api.Domains.Personas.Models;

public sealed class NarrativeTimeline
{
    public string       Era       { get; set; } = string.Empty;
    public List<string> KeyEvents { get; set; } = [];
}
