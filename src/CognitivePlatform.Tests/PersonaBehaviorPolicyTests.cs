using CognitivePlatform.Api.Domains.Personas;
using CognitivePlatform.Api.Domains.Personas.Models;

namespace CognitivePlatform.Tests;

public class PersonaBehaviorPolicyTests
{
    private readonly PersonaBehaviorPolicy _policy = new();

    // ================================================================
    // IsResurrectionZone
    // ================================================================

    [Fact]
    public void IsResurrectionZone_ReturnsTrue_WhenAllThresholdsBreached()
    {
        var configuration = new PersonaConfiguration
                            {
                                    ImmersionLevel        = 0.95f
                                  , UncertaintyVisibility = 0.05f
                                  , EmotionalReciprocity  = 0.95f
                            };

        Assert.True(_policy.IsResurrectionZone(configuration));
    }

    [Fact]
    public void IsResurrectionZone_ReturnsFalse_WhenImmersionLevelBelowThreshold()
    {
        var configuration = new PersonaConfiguration
                            {
                                    ImmersionLevel        = 0.94f
                                  , UncertaintyVisibility = 0.05f
                                  , EmotionalReciprocity  = 0.95f
                            };

        Assert.False(_policy.IsResurrectionZone(configuration));
    }

    [Fact]
    public void IsResurrectionZone_ReturnsFalse_WhenUncertaintyVisibilityAboveThreshold()
    {
        var configuration = new PersonaConfiguration
                            {
                                    ImmersionLevel        = 0.95f
                                  , UncertaintyVisibility = 0.06f
                                  , EmotionalReciprocity  = 0.95f
                            };

        Assert.False(_policy.IsResurrectionZone(configuration));
    }

    [Fact]
    public void IsResurrectionZone_ReturnsFalse_WhenEmotionalReciprocityBelowThreshold()
    {
        var configuration = new PersonaConfiguration
                            {
                                    ImmersionLevel        = 0.95f
                                  , UncertaintyVisibility = 0.05f
                                  , EmotionalReciprocity  = 0.94f
                            };

        Assert.False(_policy.IsResurrectionZone(configuration));
    }

    [Fact]
    public void IsResurrectionZone_ReturnsFalse_ForDefaultConfiguration()
    {
        Assert.False(_policy.IsResurrectionZone(PersonaConfiguration.Default));
    }

    // ================================================================
    // ApplyGuardrails
    // ================================================================

    [Fact]
    public void ApplyGuardrails_ClampsValues_WhenInResurrectionZone()
    {
        var configuration = new PersonaConfiguration
                            {
                                    ImmersionLevel        = 0.97f
                                  , UncertaintyVisibility = 0.02f
                                  , EmotionalReciprocity  = 0.98f
                            };

        var result = _policy.ApplyGuardrails(configuration);

        Assert.Equal(0.75f, result.ImmersionLevel);
        Assert.Equal(0.45f, result.UncertaintyVisibility);
        Assert.Equal(0.60f, result.EmotionalReciprocity);
    }

    [Fact]
    public void ApplyGuardrails_PreservesOtherFields_WhenClamping()
    {
        var configuration = new PersonaConfiguration
                            {
                                    ImmersionLevel               = 0.97f
                                  , UncertaintyVisibility        = 0.02f
                                  , EmotionalReciprocity         = 0.98f
                                  , HistoricalStrictness         = 0.88f
                                  , FictionalExpansion           = 0.33f
                                  , AdaptivePersonalityEvolution = false
                            };

        var result = _policy.ApplyGuardrails(configuration);

        Assert.Equal(0.88f, result.HistoricalStrictness);
        Assert.Equal(0.33f, result.FictionalExpansion);
        Assert.False(result.AdaptivePersonalityEvolution);
    }

    [Fact]
    public void ApplyGuardrails_ReturnsConfigurationUnchanged_WhenNotInResurrectionZone()
    {
        var configuration = PersonaConfiguration.Default;

        var result = _policy.ApplyGuardrails(configuration);

        Assert.Same(configuration, result);
    }

    [Fact]
    public void ApplyGuardrails_ReturnsDifferentInstance_WhenClamping()
    {
        var configuration = new PersonaConfiguration
                            {
                                    ImmersionLevel        = 0.95f
                                  , UncertaintyVisibility = 0.05f
                                  , EmotionalReciprocity  = 0.95f
                            };

        var result = _policy.ApplyGuardrails(configuration);

        Assert.NotSame(configuration, result);
    }
}
