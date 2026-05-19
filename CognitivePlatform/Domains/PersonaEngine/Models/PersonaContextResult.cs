using CognitivePlatform.Api.Domains.Personality;

namespace CognitivePlatform.Api.Domains.PersonaEngine.Models;

public class PersonaContextResult
{
    public PersonalityDefinition? Personality          { get; set; }
    public Intent                 Intent               { get; set; }
    public IntentAnalysisResult   IntentAnalysisResult { get; set; } = new();
}
