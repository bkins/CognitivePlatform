namespace CognitivePlatform.Api.Domains.Personas.Models;

public sealed class PersonaConfiguration
{
    public float ImmersionLevel               { get; set; } = 0.68f;
    public float UncertaintyVisibility        { get; set; } = 0.62f;
    public float HistoricalStrictness         { get; set; } = 0.80f;
    public float FictionalExpansion           { get; set; } = 0.40f;
    public float EmotionalReciprocity         { get; set; } = 0.48f;
    public float RelationshipContinuity       { get; set; } = 0.70f;
    public float SelfAwareness                { get; set; } = 0.45f;
    public float NarrativeRomanticism         { get; set; } = 0.42f;
    public float MemoryGapFilling             { get; set; } = 0.38f;
    public bool  AdaptivePersonalityEvolution { get; set; } = true;

    public static PersonaConfiguration Default => new();
}
