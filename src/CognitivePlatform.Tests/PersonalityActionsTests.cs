using Moq;
using CognitivePlatform.Api.Domains.Personality;

namespace CognitivePlatform.Tests;

public class PersonalityActionsTests
{
    private readonly Mock<IPersonalityService> _serviceMock = new();
    private readonly PersonalityActions        _actions;

    public PersonalityActionsTests()
    {
        _actions = new PersonalityActions(_serviceMock.Object);
    }

    // ================================================================
    // ListPersonalities
    // ================================================================

    [Fact]
    public async Task ListPersonalities_FormatsOutput_WithNameDescriptionAndActiveMarker()
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

        var result = await _actions.ListPersonalities();

        Assert.Contains("Friendly Helper",                        result);
        Assert.Contains("Programmer",                             result);
        Assert.Contains("Kind, casual, and helpful",              result);
        Assert.Contains("*(active)*",                             result);
        Assert.Contains("Personalities (2)",                      result);
    }

    [Fact]
    public async Task ListPersonalities_ReturnsNoPersonalitiesMessage_WhenListIsEmpty()
    {
        _serviceMock.Setup(service => service.GetAllAsync())
                    .ReturnsAsync(new List<PersonalityDefinition>());

        var result = await _actions.ListPersonalities();

        Assert.Equal("No personalities found.", result);
    }

    // ================================================================
    // GetActivePersonality
    // ================================================================

    [Fact]
    public async Task GetActivePersonality_ReturnsFormattedActivePersonality()
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

        var result = await _actions.GetActivePersonality();

        Assert.Contains("Zen",                                   result);
        Assert.Contains("Mindful and calm, like a wise teacher", result);
        Assert.Contains("Active Personality",                    result);
    }

    [Fact]
    public async Task GetActivePersonality_ReturnsNotSetMessage_WhenNoActivePersonality()
    {
        _serviceMock.Setup(service => service.GetActiveAsync())
                    .ReturnsAsync((PersonalityDefinition?)null);

        var result = await _actions.GetActivePersonality();

        Assert.Equal("No active personality is set.", result);
    }

    // ================================================================
    // SetPersonality
    // ================================================================

    [Fact]
    public async Task SetPersonality_CallsSetActiveAsync_WithMatchedPersonalityId()
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

        var result = await _actions.SetPersonality("Zen");

        Assert.Contains("Zen",            result);
        Assert.Contains("Mindful and calm", result);
        _serviceMock.Verify(service => service.SetActiveAsync(zenId), Times.Once);
    }

    [Fact]
    public async Task SetPersonality_IsCaseInsensitive()
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

        var result = await _actions.SetPersonality("PROGRAMMER");

        Assert.Contains("Programmer", result);
        _serviceMock.Verify(service => service.SetActiveAsync(programmerId), Times.Once);
    }

    [Fact]
    public async Task SetPersonality_ReturnsNotFoundMessage_WhenNameDoesNotMatch()
    {
        var personalities = new List<PersonalityDefinition>
                            {
                                new PersonalityDefinition { Id = Guid.NewGuid().ToString("N"), Name = "Zen", IsBuiltIn = true }
                            };

        _serviceMock.Setup(service => service.GetAllAsync())
                    .ReturnsAsync(personalities);

        var result = await _actions.SetPersonality("Nonexistent");

        Assert.Contains("No personality named 'Nonexistent'", result);
        Assert.Contains("Zen",                               result);
        _serviceMock.Verify(service => service.SetActiveAsync(It.IsAny<string>()), Times.Never);
    }
}
