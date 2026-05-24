using Moq;
using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Domains.Personas;
using CognitivePlatform.Api.Domains.Personas.Models;

namespace CognitivePlatform.Tests;

public class PhaseE_AlternateTimelineServiceTests
{
    private readonly Mock<IObjectStore>         _storeMock = new();
    private readonly AlternateTimelineService   _service;

    public PhaseE_AlternateTimelineServiceTests()
    {
        _service = new AlternateTimelineService(_storeMock.Object);
    }

    [Fact]
    public async Task CreateTimeline_AssignsNonEmptyId()
    {
        var personaId = Guid.NewGuid();

        _storeMock
            .Setup(store => store.Save(It.IsAny<AlternateTimeline>()
                                     , It.IsAny<string?>()
                                     , It.IsAny<string?>()))
            .ReturnsAsync(string.Empty);

        var timeline = await _service.CreateTimelineAsync(personaId
                                                         , "stayed in town"
                                                         , "What if she had never left?"
                                                         , DateTime.UtcNow);

        Assert.NotEqual(Guid.Empty, timeline.Id);
    }

    [Fact]
    public async Task CreateTimeline_SetsPersonaId()
    {
        var personaId = Guid.NewGuid();

        _storeMock
            .Setup(store => store.Save(It.IsAny<AlternateTimeline>()
                                     , It.IsAny<string?>()
                                     , It.IsAny<string?>()))
            .ReturnsAsync(string.Empty);

        var timeline = await _service.CreateTimelineAsync(personaId, "a name", "a premise", DateTime.UtcNow);

        Assert.Equal(personaId, timeline.PersonaId);
    }

    [Fact]
    public async Task CreateTimeline_SavesWithCorrectPartitionKey()
    {
        var personaId = Guid.NewGuid();

        _storeMock
            .Setup(store => store.Save(It.IsAny<AlternateTimeline>()
                                     , $"timeline:{personaId}"
                                     , It.IsAny<string?>()))
            .ReturnsAsync(string.Empty);

        await _service.CreateTimelineAsync(personaId, "test", "premise", DateTime.UtcNow);

        _storeMock.Verify(store => store.Save(It.IsAny<AlternateTimeline>()
                                            , $"timeline:{personaId}"
                                            , It.IsAny<string?>())
                        , Times.Once);
    }

    [Fact]
    public async Task GetTimelinesForPersona_ReturnsAllNonDeleted()
    {
        var personaId    = Guid.NewGuid();
        var activeOne    = new AlternateTimeline { Id = Guid.NewGuid(), PersonaId = personaId, IsDeleted = false, CreatedUtc = DateTime.UtcNow };
        var deletedOne   = new AlternateTimeline { Id = Guid.NewGuid(), PersonaId = personaId, IsDeleted = true,  CreatedUtc = DateTime.UtcNow };

        _storeMock
            .Setup(store => store.List<AlternateTimeline>($"timeline:{personaId}", null, null))
            .Returns([ activeOne, deletedOne ]);

        var timelines = await _service.GetTimelinesForPersonaAsync(personaId);

        Assert.Single(timelines);
        Assert.Equal(activeOne.Id, timelines[0].Id);
    }

    [Fact]
    public async Task AddEvent_AppendsEventToTimeline()
    {
        var personaId  = Guid.NewGuid();
        var timelineId = Guid.NewGuid();
        var timeline   = new AlternateTimeline { Id = timelineId, PersonaId = personaId };

        _storeMock
            .Setup(store => store.List<AlternateTimeline>(null, null, null))
            .Returns([ timeline ]);

        _storeMock
            .Setup(store => store.Save(It.IsAny<AlternateTimeline>()
                                     , It.IsAny<string?>()
                                     , It.IsAny<string?>()))
            .ReturnsAsync(string.Empty);

        var timelineEvent = new TimelineEvent
                            {
                                    EventDate       = "Summer 1995"
                                  , Description     = "They met at the lake."
                                  , EmotionalWeight = 0.9f
                                  , IsCanon         = true
                            };

        var updated = await _service.AddEventAsync(timelineId, timelineEvent);

        Assert.Single(updated.Events);
        Assert.Equal("They met at the lake.", updated.Events[0].Description);
        Assert.NotEqual(Guid.Empty, updated.Events[0].Id);
    }

    [Fact]
    public async Task GetTimeline_ReturnsTimeline_WhenFound()
    {
        var timelineId = Guid.NewGuid();
        var personaId  = Guid.NewGuid();
        var timeline   = new AlternateTimeline { Id = timelineId, PersonaId = personaId };

        _storeMock
            .Setup(store => store.List<AlternateTimeline>(null, null, null))
            .Returns([ timeline ]);

        var result = await _service.GetTimelineAsync(timelineId);

        Assert.NotNull(result);
        Assert.Equal(timelineId, result.Id);
    }

    [Fact]
    public async Task GetTimeline_ReturnsNull_WhenNotFound()
    {
        _storeMock
            .Setup(store => store.List<AlternateTimeline>(null, null, null))
            .Returns([]);

        var result = await _service.GetTimelineAsync(Guid.NewGuid());

        Assert.Null(result);
    }
}
