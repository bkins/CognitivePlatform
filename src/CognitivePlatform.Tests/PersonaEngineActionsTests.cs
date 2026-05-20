using Moq;
using CognitivePlatform.Api.Domains.Personality;
using CognitivePlatform.Api.Domains.PersonaEngine;

namespace CognitivePlatform.Tests;

public class PersonaEngineActionsTests
{
    private readonly Mock<IPersonalityService> _serviceMock = new();
    private readonly PersonaEngineActions      _actions;

    public PersonaEngineActionsTests()
    {
        _actions = new PersonaEngineActions(_serviceMock.Object);
    }

    // ================================================================
    // ListPersonas
    // ================================================================

    [Fact]
    public async Task ListPersonas_FormatsOutput_WithNameDescriptionAndActiveMarker()
    {
        var personalities = new List<PersonalityDefinition>
                            {
                                new PersonalityDefinition
                                {
                                        Name        = "Friendly Helper"
                                      , Description = "Kind, casual, and helpful"
                                      , IsBuiltIn   = true
                                      , IsActive    = true
                                }
                              , new PersonalityDefinition
                                {
                                        Name        = "Programmer"
                                      , Description = "Smart, concise, accurate technical coding help"
                                      , IsBuiltIn   = true
                                      , IsActive    = false
                                }
                            };

        _serviceMock.Setup(service => service.GetAllAsync())
                    .ReturnsAsync(personalities);

        var result = await _actions.ListPersonas();

        Assert.Contains("Friendly Helper",                        result);
        Assert.Contains("Programmer",                             result);
        Assert.Contains("Kind, casual, and helpful",              result);
        Assert.Contains("*(active)*",                             result);
        Assert.Contains("Personas (2)",                           result);
    }

    [Fact]
    public async Task ListPersonas_ReturnsNoPersonasMessage_WhenListIsEmpty()
    {
        _serviceMock.Setup(service => service.GetAllAsync())
                    .ReturnsAsync(new List<PersonalityDefinition>());

        var result = await _actions.ListPersonas();

        Assert.Equal("No personas found.", result);
    }

    // ================================================================
    // GetActivePersona
    // ================================================================

    [Fact]
    public async Task GetActivePersona_ReturnsFormattedActivePersona()
    {
        var active = new PersonalityDefinition
                     {
                             Name        = "Zen"
                           , Description = "Mindful and calm, like a wise teacher"
                           , IsBuiltIn   = true
                           , IsActive    = true
                     };

        _serviceMock.Setup(service => service.GetActiveAsync())
                    .ReturnsAsync(active);

        var result = await _actions.GetActivePersona();

        Assert.Contains("Zen",                                   result);
        Assert.Contains("Mindful and calm, like a wise teacher", result);
        Assert.Contains("Active Persona",                        result);
    }

    [Fact]
    public async Task GetActivePersona_ReturnsNotSetMessage_WhenNoActivePersona()
    {
        _serviceMock.Setup(service => service.GetActiveAsync())
                    .ReturnsAsync((PersonalityDefinition?)null);

        var result = await _actions.GetActivePersona();

        Assert.Equal("No active persona is set.", result);
    }

    // ================================================================
    // SetPersona
    // ================================================================

    [Fact]
    public async Task SetPersona_CallsSetActiveAsync_WithMatchedPersonaId()
    {
        var zenId = Guid.NewGuid().ToString("N");
        var personalities = new List<PersonalityDefinition>
                            {
                                new PersonalityDefinition { Id = zenId, Name = "Zen", Description = "Mindful and calm", IsBuiltIn = true }
                            };

        _serviceMock.Setup(service => service.GetAllAsync())
                    .ReturnsAsync(personalities);

        _serviceMock.Setup(service => service.SetActiveAsync(zenId))
                    .ReturnsAsync(personalities[0]);

        var result = await _actions.SetPersona("Zen");

        Assert.Contains("Zen",             result);
        Assert.Contains("Mindful and calm", result);
        _serviceMock.Verify(service => service.SetActiveAsync(zenId), Times.Once);
    }

    [Fact]
    public async Task SetPersona_IsCaseInsensitive()
    {
        var programmerId = Guid.NewGuid().ToString("N");
        var personalities = new List<PersonalityDefinition>
                            {
                                new PersonalityDefinition { Id = programmerId, Name = "Programmer", Description = "Coding help", IsBuiltIn = true }
                            };

        _serviceMock.Setup(service => service.GetAllAsync())
                    .ReturnsAsync(personalities);

        _serviceMock.Setup(service => service.SetActiveAsync(programmerId))
                    .ReturnsAsync(personalities[0]);

        var result = await _actions.SetPersona("PROGRAMMER");

        Assert.Contains("Programmer", result);
        _serviceMock.Verify(service => service.SetActiveAsync(programmerId), Times.Once);
    }

    [Fact]
    public async Task SetPersona_ReturnsNotFoundMessage_WhenNameDoesNotMatch()
    {
        var personalities = new List<PersonalityDefinition>
                            {
                                new PersonalityDefinition { Id = Guid.NewGuid().ToString("N"), Name = "Zen", IsBuiltIn = true }
                            };

        _serviceMock.Setup(service => service.GetAllAsync())
                    .ReturnsAsync(personalities);

        var result = await _actions.SetPersona("Nonexistent");

        Assert.Contains("No persona named 'Nonexistent'", result);
        Assert.Contains("Zen",                            result);
        _serviceMock.Verify(service => service.SetActiveAsync(It.IsAny<string>()), Times.Never);
    }
}
