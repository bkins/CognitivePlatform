namespace CognitivePlatform.Api.Domains.Personas.Models;

public class CanonRules
{
    public bool  AllowAIInference         { get; set; } = true;
    public bool  AllowNarrativeExpansion  { get; set; } = true;
    public float MinConfidenceToCanonize  { get; set; } = 0.80f;
}
