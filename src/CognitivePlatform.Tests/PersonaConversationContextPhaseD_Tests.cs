using Moq;
using CognitivePlatform.Api.Domains.Personas;
using CognitivePlatform.Api.Domains.Personas.Models;

namespace CognitivePlatform.Tests;

public class PersonaConversationContextPhaseD_Tests
{
    [Fact]
    public void PersonaConversationContext_PreferredModelName_DefaultsToNull()
    {
        var context = new PersonaConversationContext();

        Assert.Null(context.PreferredModelName);
    }

    [Fact]
    public void PersonaConversationContext_PromptAdapted_DefaultsToFalse()
    {
        var context = new PersonaConversationContext();

        Assert.False(context.PromptAdapted);
    }

    [Fact]
    public async Task BuildConversationContextAsync_SetsPreferredModelName_WhenSelectorPresent()
    {
        var personaId = Guid.NewGuid();
        var persona   = new CanonicalPersona { Id = personaId, Name = "Iris" };

        var serviceMock  = new Mock<IPersonaService>();
        var storeMock    = new Mock<IPersonaStore>();
        var selectorMock = new Mock<IPersonaModelSelector>();
        var policy       = new PersonaBehaviorPolicy();

        serviceMock.Setup(service => service.GetAsync(personaId, It.IsAny<CancellationToken>()))
                   .ReturnsAsync(persona);

        storeMock.Setup(store => store.GetMemoriesAsync(personaId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync([]);

        selectorMock.Setup(selector => selector.SelectModel(It.IsAny<CanonicalPersona>()
                                                          , It.IsAny<PersonaStabilityScore>()))
                    .Returns("groq/llama-3.3-70b");

        var runtime = new PersonaRuntime(serviceMock.Object
                                       , storeMock.Object
                                       , policy
                                       , selectorMock.Object);

        var result = await runtime.BuildConversationContextAsync(personaId, "hello");

        Assert.Equal("groq/llama-3.3-70b", result.PreferredModelName);
    }

    [Fact]
    public async Task BuildConversationContextAsync_SetsPromptAdapted_WhenProfileMatchFound()
    {
        var personaId = Guid.NewGuid();
        var persona   = new CanonicalPersona { Id = personaId, Name = "Mira" };
        var profile   = new ModelCapabilityProfile
                        {
                                ModelName            = "strict-model"
                              , RestrictionLevel     = ProviderRestrictionLevel.Strict
                              , InjectsSafetyDisclaimers = true
                        };

        var serviceMock  = new Mock<IPersonaService>();
        var storeMock    = new Mock<IPersonaStore>();
        var selectorMock = new Mock<IPersonaModelSelector>();
        var registryMock = new Mock<IModelCapabilityRegistry>();
        var policy       = new PersonaBehaviorPolicy();

        serviceMock.Setup(service => service.GetAsync(personaId, It.IsAny<CancellationToken>()))
                   .ReturnsAsync(persona);

        storeMock.Setup(store => store.GetMemoriesAsync(personaId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync([]);

        selectorMock.Setup(selector => selector.SelectModel(It.IsAny<CanonicalPersona>()
                                                          , It.IsAny<PersonaStabilityScore>()))
                    .Returns("strict-model");

        registryMock.Setup(registry => registry.GetByModel("strict-model"))
                    .Returns(profile);

        selectorMock.Setup(selector => selector.AdaptPromptForProvider(It.IsAny<string>(), profile))
                    .Returns<string, ModelCapabilityProfile>((prompt, _) => prompt + " [adapted]");

        var runtime = new PersonaRuntime(serviceMock.Object
                                       , storeMock.Object
                                       , policy
                                       , selectorMock.Object
                                       , registryMock.Object);

        var result = await runtime.BuildConversationContextAsync(personaId, "hello");

        Assert.True(result.PromptAdapted);
        Assert.Contains("[adapted]", result.SystemPrompt);
    }

    [Fact]
    public async Task BuildConversationContextAsync_PromptAdaptedFalse_WhenNoSelectorPresent()
    {
        var personaId = Guid.NewGuid();
        var persona   = new CanonicalPersona { Id = personaId, Name = "Zoe" };

        var serviceMock = new Mock<IPersonaService>();
        var storeMock   = new Mock<IPersonaStore>();
        var policy      = new PersonaBehaviorPolicy();

        serviceMock.Setup(service => service.GetAsync(personaId, It.IsAny<CancellationToken>()))
                   .ReturnsAsync(persona);

        storeMock.Setup(store => store.GetMemoriesAsync(personaId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync([]);

        var runtime = new PersonaRuntime(serviceMock.Object, storeMock.Object, policy);

        var result = await runtime.BuildConversationContextAsync(personaId, "hello");

        Assert.Null(result.PreferredModelName);
        Assert.False(result.PromptAdapted);
    }
}
