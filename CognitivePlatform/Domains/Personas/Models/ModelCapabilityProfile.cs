namespace CognitivePlatform.Api.Domains.Personas.Models;

public class ModelCapabilityProfile
{
    public string                  ProviderName                 { get; set; } = string.Empty;
    public string                  ModelName                    { get; set; } = string.Empty;
    public float                   EmotionalAttachmentTolerance { get; set; }
    public float                   RomanceTolerance             { get; set; }
    public float                   RoleplayTolerance            { get; set; }
    public float                   MemoryContinuityTolerance    { get; set; }
    public bool                    InjectsSafetyDisclaimers     { get; set; }
    public ProviderRestrictionLevel RestrictionLevel            { get; set; }
}

public enum ProviderRestrictionLevel
{
    Permissive
  , Moderate
  , Strict
}
