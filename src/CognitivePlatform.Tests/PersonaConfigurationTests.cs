using CognitivePlatform.Api.Domains.Personas.Models;

namespace CognitivePlatform.Tests;

public class PersonaConfigurationTests
{
    [Fact]
    public void Default_HasExpectedImmersionLevel()
    {
        var config = PersonaConfiguration.Default;

        Assert.Equal(0.68f, config.ImmersionLevel);
    }

    [Fact]
    public void Default_HasAllExpectedValues()
    {
        var config = PersonaConfiguration.Default;

        Assert.Equal(0.68f, config.ImmersionLevel);
        Assert.Equal(0.62f, config.UncertaintyVisibility);
        Assert.Equal(0.80f, config.HistoricalStrictness);
        Assert.Equal(0.40f, config.FictionalExpansion);
        Assert.Equal(0.48f, config.EmotionalReciprocity);
        Assert.Equal(0.70f, config.RelationshipContinuity);
        Assert.Equal(0.45f, config.SelfAwareness);
        Assert.Equal(0.42f, config.NarrativeRomanticism);
        Assert.Equal(0.38f, config.MemoryGapFilling);
        Assert.True(config.AdaptivePersonalityEvolution);
    }

    [Fact]
    public void Default_IsNotInResurrectionZone()
    {
        var config            = PersonaConfiguration.Default;
        var inResurrectionZone = config.ImmersionLevel       >= 0.95f
                              && config.UncertaintyVisibility <= 0.05f
                              && config.EmotionalReciprocity  >= 0.95f;

        Assert.False(inResurrectionZone);
    }

    [Fact]
    public void ResurrectionZone_IsDetected_WhenAllThresholdsMet()
    {
        var config = new PersonaConfiguration
                     {
                             ImmersionLevel        = 0.95f
                           , UncertaintyVisibility = 0.05f
                           , EmotionalReciprocity  = 0.95f
                     };

        var inResurrectionZone = config.ImmersionLevel       >= 0.95f
                              && config.UncertaintyVisibility <= 0.05f
                              && config.EmotionalReciprocity  >= 0.95f;

        Assert.True(inResurrectionZone);
    }

    [Fact]
    public void ResurrectionZone_IsNotDetected_WhenOnlyImmersionIsHigh()
    {
        var config = new PersonaConfiguration
                     {
                             ImmersionLevel        = 0.95f
                           , UncertaintyVisibility = 0.50f
                           , EmotionalReciprocity  = 0.50f
                     };

        var inResurrectionZone = config.ImmersionLevel       >= 0.95f
                              && config.UncertaintyVisibility <= 0.05f
                              && config.EmotionalReciprocity  >= 0.95f;

        Assert.False(inResurrectionZone);
    }
}
