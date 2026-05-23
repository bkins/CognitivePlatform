using CognitivePlatform.Api.Domains.Personas.Models;

namespace CognitivePlatform.Api.Domains.Personas;

public class PersonaBehaviorPolicy : IPersonaBehaviorPolicy
{
    private const float ImmersionZoneThreshold         = 0.95f;
    private const float UncertaintyZoneThreshold       = 0.05f;
    private const float ReciprocityZoneThreshold       = 0.95f;

    private const float SafeImmersionLevel             = 0.75f;
    private const float SafeUncertaintyVisibility      = 0.45f;
    private const float SafeEmotionalReciprocity       = 0.60f;

    public bool IsResurrectionZone(PersonaConfiguration configuration)
    {
        return configuration.ImmersionLevel       >= ImmersionZoneThreshold
            && configuration.UncertaintyVisibility <= UncertaintyZoneThreshold
            && configuration.EmotionalReciprocity  >= ReciprocityZoneThreshold;
    }

    public PersonaConfiguration ApplyGuardrails(PersonaConfiguration configuration)
    {
        if (!IsResurrectionZone(configuration))
            return configuration;

        return new PersonaConfiguration
               {
                       ImmersionLevel               = SafeImmersionLevel
                     , UncertaintyVisibility        = SafeUncertaintyVisibility
                     , EmotionalReciprocity         = SafeEmotionalReciprocity
                     , HistoricalStrictness         = configuration.HistoricalStrictness
                     , FictionalExpansion           = configuration.FictionalExpansion
                     , RelationshipContinuity       = configuration.RelationshipContinuity
                     , SelfAwareness                = configuration.SelfAwareness
                     , NarrativeRomanticism         = configuration.NarrativeRomanticism
                     , MemoryGapFilling             = configuration.MemoryGapFilling
                     , AdaptivePersonalityEvolution = configuration.AdaptivePersonalityEvolution
               };
    }
}
