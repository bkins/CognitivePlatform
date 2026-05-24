using Moq;
using CognitivePlatform.Api.Domains.Personas;
using CognitivePlatform.Api.Domains.Personas.Models;
using CognitivePlatform.Api.Interpreter;

namespace CognitivePlatform.Tests;

public class PersonaModelSelectorTests
{
    private readonly Mock<IModelCapabilityRegistry> _registryMock = new();
    private readonly Mock<ILlmCapacityRouter>       _routerMock   = new();
    private readonly PersonaModelSelector           _selector;

    public PersonaModelSelectorTests()
    {
        _selector = new PersonaModelSelector(_registryMock.Object, _routerMock.Object);
    }

    [Fact]
    public void SelectModel_ReturnsPermissiveModel_WhenStabilityLow()
    {
        var persona    = new CanonicalPersona { Id = Guid.NewGuid(), Name = "Aria" };
        var lowScore   = new PersonaStabilityScore { PersonaId = persona.Id, Score = 0.3f };
        var permissive = new ModelCapabilityProfile
                         {
                                 ModelName        = "llama-3.3-70b"
                               , RestrictionLevel = ProviderRestrictionLevel.Permissive
                         };

        _registryMock.Setup(registry => registry.GetByRestrictionLevel(ProviderRestrictionLevel.Permissive))
                     .Returns([permissive]);

        var result = _selector.SelectModel(persona, lowScore);

        Assert.Equal("llama-3.3-70b", result);
    }

    [Fact]
    public void SelectModel_PrefersHighEmotionalTolerance_WhenEmotionalReciprocityHigh()
    {
        var persona = new CanonicalPersona
                      {
                              Id            = Guid.NewGuid()
                            , Name         = "Luna"
                            , Configuration = new PersonaConfiguration { EmotionalReciprocity = 0.85f }
                      };
        var score   = new PersonaStabilityScore { PersonaId = persona.Id, Score = 1.0f };
        var match   = new ModelCapabilityProfile
                      {
                              ModelName                    = "emotional-model"
                            , EmotionalAttachmentTolerance = 0.90f
                            , RestrictionLevel             = ProviderRestrictionLevel.Moderate
                      };

        _registryMock.Setup(registry => registry.GetByRestrictionLevel(It.IsAny<ProviderRestrictionLevel>()))
                     .Returns([]);
        _registryMock.Setup(registry => registry.GetAll())
                     .Returns([match]);

        var result = _selector.SelectModel(persona, score);

        Assert.Equal("emotional-model", result);
    }

    [Fact]
    public void SelectModel_PrefersRomanceTolerance_WhenNarrativeRomanticismHigh()
    {
        var persona = new CanonicalPersona
                      {
                              Id            = Guid.NewGuid()
                            , Name         = "Evelyn"
                            , Configuration = new PersonaConfiguration
                                             {
                                                     NarrativeRomanticism = 0.80f
                                                   , EmotionalReciprocity = 0.50f
                                             }
                      };
        var score   = new PersonaStabilityScore { PersonaId = persona.Id, Score = 1.0f };
        var match   = new ModelCapabilityProfile
                      {
                              ModelName        = "romance-model"
                            , RomanceTolerance = 0.75f
                            , RestrictionLevel = ProviderRestrictionLevel.Permissive
                      };

        _registryMock.Setup(registry => registry.GetByRestrictionLevel(It.IsAny<ProviderRestrictionLevel>()))
                     .Returns([]);
        _registryMock.Setup(registry => registry.GetAll())
                     .Returns([match]);

        var result = _selector.SelectModel(persona, score);

        Assert.Equal("romance-model", result);
    }

    [Fact]
    public void SelectModel_FallsBackToCapacityRouter_WhenNoRegistryMatch()
    {
        var persona = new CanonicalPersona
                      {
                              Id            = Guid.NewGuid()
                            , Name         = "Rex"
                            , Configuration = PersonaConfiguration.Default
                      };
        var score   = new PersonaStabilityScore { PersonaId = persona.Id, Score = 1.0f };
        var capacity = new LlmModelCapacity { ModelId = new LlmModelId("groq", "llama-3.3-70b") };

        _registryMock.Setup(registry => registry.GetByRestrictionLevel(It.IsAny<ProviderRestrictionLevel>()))
                     .Returns([]);
        _registryMock.Setup(registry => registry.GetAll())
                     .Returns([]);
        _routerMock.Setup(router => router.SelectModel(TaskComplexity.Standard))
                   .Returns(capacity);

        var result = _selector.SelectModel(persona, score);

        Assert.Equal("llama-3.3-70b", result);
    }

    [Fact]
    public void AdaptPromptForProvider_AppendsInstruction_WhenInjectsSafetyDisclaimers()
    {
        var profile = new ModelCapabilityProfile
                      {
                              ModelName            = "gpt-4o-mini"
                            , InjectsSafetyDisclaimers = true
                            , RestrictionLevel     = ProviderRestrictionLevel.Moderate
                      };

        var result = _selector.AdaptPromptForProvider("You are embodying Clara.", profile);

        Assert.Contains("narrative", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("You are embodying Clara.", result);
    }

    [Fact]
    public void AdaptPromptForProvider_ReturnsUnchanged_WhenPermissive()
    {
        var profile = new ModelCapabilityProfile
                      {
                              ModelName        = "llama-3.3-70b"
                            , RestrictionLevel = ProviderRestrictionLevel.Permissive
                      };
        const string original = "You are embodying Dana.";

        var result = _selector.AdaptPromptForProvider(original, profile);

        Assert.Equal(original, result);
    }
}
