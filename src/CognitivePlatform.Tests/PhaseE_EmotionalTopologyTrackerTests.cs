using Moq;
using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Domains.Personas;
using CognitivePlatform.Api.Domains.Personas.Models;

namespace CognitivePlatform.Tests;

public class PhaseE_EmotionalTopologyTrackerTests
{
    private readonly Mock<IObjectStore>       _storeMock = new();
    private readonly EmotionalTopologyTracker _tracker;

    public PhaseE_EmotionalTopologyTrackerTests()
    {
        _tracker = new EmotionalTopologyTracker(_storeMock.Object);
    }

    [Fact]
    public async Task RecordSampleAsync_SavesPoint_WithAssignedId()
    {
        var personaId = Guid.NewGuid();
        var point     = new EmotionalTopologyPoint
                        {
                                PersonaId      = personaId
                              , ConversationId = "conv-1"
                              , WarmthScore    = 0.8f
                        };

        _storeMock
            .Setup(store => store.Save(It.IsAny<EmotionalTopologyPoint>()
                                     , It.IsAny<string>()
                                     , It.IsAny<string>()))
            .ReturnsAsync(string.Empty);

        await _tracker.RecordSampleAsync(point);

        Assert.NotEqual(Guid.Empty, point.Id);
        _storeMock.Verify(store => store.Save(It.Is<EmotionalTopologyPoint>(recorded =>
                                                  recorded.PersonaId == personaId)
                                            , $"topology:{personaId}"
                                            , It.IsAny<string>())
                        , Times.Once);
    }

    [Fact]
    public async Task RecordSampleAsync_SetsTimestamp_WhenNotProvided()
    {
        var point = new EmotionalTopologyPoint { PersonaId = Guid.NewGuid() };

        _storeMock
            .Setup(store => store.Save(It.IsAny<EmotionalTopologyPoint>()
                                     , It.IsAny<string>()
                                     , It.IsAny<string>()))
            .ReturnsAsync(string.Empty);

        var before = DateTime.UtcNow;
        await _tracker.RecordSampleAsync(point);
        var after = DateTime.UtcNow;

        Assert.InRange(point.SampledUtc, before, after);
    }

    [Fact]
    public async Task GetTopologyAsync_ReturnsPointsInChronologicalOrder()
    {
        var personaId = Guid.NewGuid();
        var earlier   = new EmotionalTopologyPoint { Id = Guid.NewGuid(), PersonaId = personaId, SampledUtc = DateTime.UtcNow.AddMinutes(-10) };
        var later     = new EmotionalTopologyPoint { Id = Guid.NewGuid(), PersonaId = personaId, SampledUtc = DateTime.UtcNow };

        _storeMock
            .Setup(store => store.List<EmotionalTopologyPoint>($"topology:{personaId}"))
            .Returns([ later, earlier ]);

        var topology = (await _tracker.GetTopologyAsync(personaId)).ToList();

        Assert.Equal(2, topology.Count);
        Assert.Equal(earlier.Id, topology[0].Id);
        Assert.Equal(later.Id,   topology[1].Id);
    }

    [Fact]
    public async Task GetTopologyAsync_ReturnsEmpty_WhenNoneRecorded()
    {
        var personaId = Guid.NewGuid();

        _storeMock
            .Setup(store => store.List<EmotionalTopologyPoint>($"topology:{personaId}"))
            .Returns([]);

        var topology = await _tracker.GetTopologyAsync(personaId);

        Assert.Empty(topology);
    }

    [Fact]
    public async Task GetLatestSampleAsync_ReturnsNewestPoint()
    {
        var personaId = Guid.NewGuid();
        var older     = new EmotionalTopologyPoint { Id = Guid.NewGuid(), PersonaId = personaId, SampledUtc = DateTime.UtcNow.AddHours(-1) };
        var newest    = new EmotionalTopologyPoint { Id = Guid.NewGuid(), PersonaId = personaId, SampledUtc = DateTime.UtcNow };

        _storeMock
            .Setup(store => store.List<EmotionalTopologyPoint>($"topology:{personaId}"))
            .Returns([ older, newest ]);

        var latest = await _tracker.GetLatestSampleAsync(personaId);

        Assert.NotNull(latest);
        Assert.Equal(newest.Id, latest.Id);
    }

    [Fact]
    public async Task GetLatestSampleAsync_ReturnsNull_WhenNoneRecorded()
    {
        var personaId = Guid.NewGuid();

        _storeMock
            .Setup(store => store.List<EmotionalTopologyPoint>($"topology:{personaId}"))
            .Returns([]);

        var latest = await _tracker.GetLatestSampleAsync(personaId);

        Assert.Null(latest);
    }
}
