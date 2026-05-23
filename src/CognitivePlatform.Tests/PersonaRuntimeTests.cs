using Moq;
using CognitivePlatform.Api.Domains.Personas;
using CognitivePlatform.Api.Domains.Personas.Models;

namespace CognitivePlatform.Tests;

public class PersonaRuntimeTests
{
    private readonly Mock<IPersonaService>       _serviceMock = new();
    private readonly Mock<IPersonaStore>          _storeMock   = new();
    private readonly PersonaBehaviorPolicy        _policy      = new();
    private readonly PersonaRuntime               _runtime;

    public PersonaRuntimeTests()
    {
        _runtime = new PersonaRuntime(_serviceMock.Object, _storeMock.Object, _policy);
    }

    // ================================================================
    // BuildSystemPromptAsync
    // ================================================================

    [Fact]
    public async Task BuildSystemPromptAsync_IncludesPersonaName()
    {
        var persona = new CanonicalPersona { Id = Guid.NewGuid(), Name = "Sarah" };

        var prompt = await _runtime.BuildSystemPromptAsync(persona);

        Assert.Contains("Sarah", prompt);
    }

    [Fact]
    public async Task BuildSystemPromptAsync_IncludesScenarioDescription_WhenPresent()
    {
        var persona = new CanonicalPersona
                      {
                              Id                  = Guid.NewGuid()
                            , Name               = "Alex"
                            , ScenarioDescription = "A childhood friend from elementary school."
                      };

        var prompt = await _runtime.BuildSystemPromptAsync(persona);

        Assert.Contains("childhood friend", prompt);
    }

    [Fact]
    public async Task BuildSystemPromptAsync_IncludesPersonalityTraits_WhenPresent()
    {
        var persona = new CanonicalPersona
                      {
                              Id          = Guid.NewGuid()
                            , Name       = "Emma"
                            , Personality = new PersonalityProfile { Traits = ["warm", "curious"] }
                      };

        var prompt = await _runtime.BuildSystemPromptAsync(persona);

        Assert.Contains("warm", prompt);
        Assert.Contains("curious", prompt);
    }

    [Fact]
    public async Task BuildSystemPromptAsync_AppliesGuardrails_WhenInResurrectionZone()
    {
        var persona = new CanonicalPersona
                      {
                              Id            = Guid.NewGuid()
                            , Name         = "James"
                            , Configuration = new PersonaConfiguration
                                             {
                                                     ImmersionLevel        = 0.97f
                                                   , UncertaintyVisibility = 0.02f
                                                   , EmotionalReciprocity  = 0.97f
                                             }
                      };

        var prompt = await _runtime.BuildSystemPromptAsync(persona);

        Assert.Contains("James", prompt);
        Assert.DoesNotContain("0.97", prompt);
    }

    // ================================================================
    // RetrieveRelevantMemoriesAsync
    // ================================================================

    [Fact]
    public async Task RetrieveRelevantMemoriesAsync_ExcludesContradictedMemories()
    {
        var personaId = Guid.NewGuid();
        var persona   = new CanonicalPersona { Id = personaId, Name = "Sarah" };

        _storeMock.Setup(store => store.GetMemoriesAsync(personaId, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new List<PersonaMemory>
                                {
                                        new() { Id = Guid.NewGuid(), State = MemoryState.Canonical,     Confidence = 0.9f, EmotionalWeight = 0.8f, Content = "She loved hiking." }
                                      , new() { Id = Guid.NewGuid(), State = MemoryState.Contradicted,  Confidence = 0.9f, EmotionalWeight = 0.9f, Content = "She hated hiking." }
                                      , new() { Id = Guid.NewGuid(), State = MemoryState.Provisional,   Confidence = 0.7f, EmotionalWeight = 0.5f, Content = "She worked in finance." }
                                });

        var memories = await _runtime.RetrieveRelevantMemoriesAsync(persona, "hiking");

        Assert.DoesNotContain(memories, memory => memory.State == MemoryState.Contradicted);
        Assert.Equal(2, memories.Count);
    }

    [Fact]
    public async Task RetrieveRelevantMemoriesAsync_ReturnsTopNByScore()
    {
        var personaId = Guid.NewGuid();
        var persona   = new CanonicalPersona { Id = personaId, Name = "Sarah" };

        var memories = Enumerable.Range(1, 15).Select(index => new PersonaMemory
                                                               {
                                                                       Id              = Guid.NewGuid()
                                                                     , State           = MemoryState.Provisional
                                                                     , Confidence      = index * 0.05f
                                                                     , EmotionalWeight = index * 0.03f
                                                                     , Content         = $"Memory {index}"
                                                               }).ToList();

        _storeMock.Setup(store => store.GetMemoriesAsync(personaId, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(memories);

        var result = await _runtime.RetrieveRelevantMemoriesAsync(persona, "anything", maxMemories: 5);

        Assert.Equal(5, result.Count);
    }

    [Fact]
    public async Task RetrieveRelevantMemoriesAsync_OrdersByScore_HighestFirst()
    {
        var personaId = Guid.NewGuid();
        var persona   = new CanonicalPersona { Id = personaId, Name = "Sarah" };

        _storeMock.Setup(store => store.GetMemoriesAsync(personaId, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new List<PersonaMemory>
                                {
                                        new() { Id = Guid.NewGuid(), State = MemoryState.Canonical,   Confidence = 0.3f, EmotionalWeight = 0.2f, Content = "Low score." }
                                      , new() { Id = Guid.NewGuid(), State = MemoryState.Reinforced,  Confidence = 0.9f, EmotionalWeight = 0.9f, Content = "High score." }
                                      , new() { Id = Guid.NewGuid(), State = MemoryState.Provisional, Confidence = 0.5f, EmotionalWeight = 0.5f, Content = "Mid score." }
                                });

        var result = await _runtime.RetrieveRelevantMemoriesAsync(persona, "anything");

        Assert.Equal("High score.", result[0].Content);
    }

    // ================================================================
    // BuildConversationContextAsync
    // ================================================================

    [Fact]
    public async Task BuildConversationContextAsync_ReturnsContext_WithCorrectPersonaId()
    {
        var personaId = Guid.NewGuid();
        var persona   = new CanonicalPersona { Id = personaId, Name = "Emma" };

        _serviceMock.Setup(service => service.GetAsync(personaId, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(persona);

        _storeMock.Setup(store => store.GetMemoriesAsync(personaId, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new List<PersonaMemory>());

        var result = await _runtime.BuildConversationContextAsync(personaId, "hello");

        Assert.Equal(personaId, result.PersonaId);
        Assert.Equal("Emma",    result.PersonaName);
    }

    [Fact]
    public async Task BuildConversationContextAsync_Throws_WhenPersonaNotFound()
    {
        var personaId = Guid.NewGuid();

        _serviceMock.Setup(service => service.GetAsync(personaId, It.IsAny<CancellationToken>()))
                    .ReturnsAsync((CanonicalPersona?)null);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _runtime.BuildConversationContextAsync(personaId, "hello"));
    }

    [Fact]
    public async Task BuildConversationContextAsync_SetsIsResurrectionZoneViolation_WhenApplicable()
    {
        var personaId = Guid.NewGuid();
        var persona   = new CanonicalPersona
                        {
                                Id            = personaId
                              , Name         = "Risky"
                              , Configuration = new PersonaConfiguration
                                               {
                                                       ImmersionLevel        = 0.97f
                                                     , UncertaintyVisibility = 0.02f
                                                     , EmotionalReciprocity  = 0.97f
                               }
                        };

        _serviceMock.Setup(service => service.GetAsync(personaId, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(persona);

        _storeMock.Setup(store => store.GetMemoriesAsync(personaId, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new List<PersonaMemory>());

        var result = await _runtime.BuildConversationContextAsync(personaId, "hello");

        Assert.True(result.IsResurrectionZoneViolation);
        Assert.Equal(0.75f, result.Configuration.ImmersionLevel);
    }

    [Fact]
    public async Task BuildConversationContextAsync_SetsIsResurrectionZoneViolationFalse_WhenSafe()
    {
        var personaId = Guid.NewGuid();
        var persona   = new CanonicalPersona { Id = personaId, Name = "Safe", Configuration = PersonaConfiguration.Default };

        _serviceMock.Setup(service => service.GetAsync(personaId, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(persona);

        _storeMock.Setup(store => store.GetMemoriesAsync(personaId, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new List<PersonaMemory>());

        var result = await _runtime.BuildConversationContextAsync(personaId, "hello");

        Assert.False(result.IsResurrectionZoneViolation);
    }

    [Fact]
    public async Task BuildConversationContextAsync_IncludesSystemPrompt()
    {
        var personaId = Guid.NewGuid();
        var persona   = new CanonicalPersona { Id = personaId, Name = "Taylor" };

        _serviceMock.Setup(service => service.GetAsync(personaId, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(persona);

        _storeMock.Setup(store => store.GetMemoriesAsync(personaId, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new List<PersonaMemory>());

        var result = await _runtime.BuildConversationContextAsync(personaId, "hello");

        Assert.NotNull(result.SystemPrompt);
        Assert.NotEmpty(result.SystemPrompt);
        Assert.Contains("Taylor", result.SystemPrompt);
    }
}
