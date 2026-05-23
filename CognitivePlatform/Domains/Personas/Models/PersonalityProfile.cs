namespace CognitivePlatform.Api.Domains.Personas.Models;

public class PersonalityProfile
{
    public string       Name          { get; set; } = string.Empty;
    public string       Description   { get; set; } = string.Empty;
    public List<string> Traits        { get; set; } = [];
    public string       EmotionalTone { get; set; } = string.Empty;
}
