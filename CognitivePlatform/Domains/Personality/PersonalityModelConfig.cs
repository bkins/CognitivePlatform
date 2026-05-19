namespace CognitivePlatform.Api.Domains.Personality;

public class PersonalityModelConfig
{
    public string? Provider             { get; set; }
    public string? ModelId              { get; set; }
    public string? SystemPromptOverride { get; set; }
    public float?  Temperature          { get; set; }
}
