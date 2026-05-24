using Moq;
using CognitivePlatform.Api.Domains.Personas;
using CognitivePlatform.Api.Domains.Personas.Models;

namespace CognitivePlatform.Tests;

public class PhaseE_PersonaRuntimeDreamModeTests
{
    private readonly Mock<IPersonaService>   _personaServiceMock = new();
    private readonly Mock<IPersonaStore>     _personaStoreMock   = new();
    private readonly PersonaBehaviorPolicy   _policy             = new();

    private CanonicalPersona MakePersona(Guid id, string name = "Elara")
    {
        var persona    = new CanonicalPersona { Id = id, Name = name };
        persona.Personality    = new PersonalityProfile();
        persona.RelationshipState = new RelationshipModel();
        persona.EmotionalState    = new EmotionalModel();
        return persona;
    }

    [Fact]
    public async Task BuildConversationContextAsync_UsesDreamPrompt_WhenDreamMode()
    {
        var personaId = Guid.NewGuid();
        var persona   = MakePersona(personaId);

        _personaServiceMock
            .Setup(service => service.GetAsync(personaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(persona);

        _personaStoreMock
            .Setup(store => store.GetMemoriesAsync(personaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var dreamAdapterMock = new Mock<IDreamModeAdapter>();
        dreamAdapterMock
            .Setup(adapter => adapter.BuildDreamPrompt(It.IsAny<string>()
                                                      , It.IsAny<CanonicalPersona>()
                                                      , It.IsAny<IEnumerable<PersonaMemory>>()))
            .Returns("[dream-framed prompt]");

        var runtime = new PersonaRuntime(_personaServiceMock.Object
                                       , _personaStoreMock.Object
                                       , _policy
                                       , dreamModeAdapter: dreamAdapterMock.Object);

        var context = await runtime.BuildConversationContextAsync(personaId
                                                                , "hello"
                                                                , ConversationMode.DreamMode);

        Assert.Equal("[dream-framed prompt]", context.SystemPrompt);
        Assert.True(context.PromptAdapted);
        Assert.Equal(ConversationMode.DreamMode, context.ActiveMode);
    }

    [Fact]
    public async Task BuildConversationContextAsync_DoesNotUseDreamPrompt_WhenFreeConversation()
    {
        var personaId = Guid.NewGuid();
        var persona   = MakePersona(personaId);

        _personaServiceMock
            .Setup(service => service.GetAsync(personaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(persona);

        _personaStoreMock
            .Setup(store => store.GetMemoriesAsync(personaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var dreamAdapterMock = new Mock<IDreamModeAdapter>();

        var runtime = new PersonaRuntime(_personaServiceMock.Object
                                       , _personaStoreMock.Object
                                       , _policy
                                       , dreamModeAdapter: dreamAdapterMock.Object);

        var context = await runtime.BuildConversationContextAsync(personaId
                                                                , "hello"
                                                                , ConversationMode.FreeConversation);

        dreamAdapterMock.Verify(adapter => adapter.BuildDreamPrompt(It.IsAny<string>()
                                                                   , It.IsAny<CanonicalPersona>()
                                                                   , It.IsAny<IEnumerable<PersonaMemory>>())
                               , Times.Never);

        Assert.Equal(ConversationMode.FreeConversation, context.ActiveMode);
    }

    [Fact]
    public async Task BuildConversationContextAsync_SetsActiveModeOnContext()
    {
        var personaId = Guid.NewGuid();
        var persona   = MakePersona(personaId);

        _personaServiceMock
            .Setup(service => service.GetAsync(personaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(persona);

        _personaStoreMock
            .Setup(store => store.GetMemoriesAsync(personaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var runtime = new PersonaRuntime(_personaServiceMock.Object
                                       , _personaStoreMock.Object
                                       , _policy);

        var context = await runtime.BuildConversationContextAsync(personaId
                                                                , "hello"
                                                                , ConversationMode.Reflection);

        Assert.Equal(ConversationMode.Reflection, context.ActiveMode);
    }
}
