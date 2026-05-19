namespace CognitivePlatform.Api.Domains.PersonaEngine.Models;

public class IntentAnalysisResult
{
    public Intent                     Intent               { get; set; } = Intent.Unknown;
    public double                     Confidence           { get; set; }
    public string?                    SuggestedPersonaName { get; set; }
    public Dictionary<string, string> Metadata             { get; set; } = new();
}
