using Moq;
using CognitivePlatform.Api.Domains.Personas;
using CognitivePlatform.Api.Domains.Personas.Models;

namespace CognitivePlatform.Tests;

public class PersonaServiceTests
{
    private readonly Mock<IPersonaStore> _storeMock = new();
    private readonly PersonaService      _service;

    public PersonaServiceTests()
    {
        _storeMock.Setup(store => store.SaveAsync(It.IsAny<CanonicalPersona>()
                                                , It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);

        _storeMock.Setup(store => store.AddMemoryAsync(It.IsAny<PersonaMemory>()
                                                     , It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);

        _storeMock.Setup(store => store.SaveSnapshotAsync(It.IsAny<MemorySnapshot>()
                                                        , It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);

        _service = new PersonaService(_storeMock.Object);
    }

    // ================================================================
    // CreateAsync
    // ================================================================

    [Fact]
    public async Task CreateAsync_AssignsNewId()
    {
        var persona = await _service.CreateAsync("Sarah", null);

        Assert.NotEqual(Guid.Empty, persona.Id);
    }

    [Fact]
    public async Task CreateAsync_SetsNameAndScenarioDescription()
    {
        var persona = await _service.CreateAsync("Sarah", "A childhood friend.");

        Assert.Equal("Sarah",               persona.Name);
        Assert.Equal("A childhood friend.", persona.ScenarioDescription);
    }

    [Fact]
    public async Task CreateAsync_SetsDefaultConfiguration()
    {
        var persona = await _service.CreateAsync("Sarah", null);

        Assert.Equal(0.68f, persona.Configuration.ImmersionLevel);
        Assert.Equal(0.80f, persona.Configuration.HistoricalStrictness);
        Assert.True(persona.Configuration.AdaptivePersonalityEvolution);
    }

    [Fact]
    public async Task CreateAsync_SetsCreatedUtcAndLastModifiedUtc()
    {
        var before  = DateTime.UtcNow.AddSeconds(-1);
        var persona = await _service.CreateAsync("Sarah", null);
        var after   = DateTime.UtcNow.AddSeconds(1);

        Assert.InRange(persona.CreatedUtc,      before, after);
        Assert.InRange(persona.LastModifiedUtc, before, after);
    }

    [Fact]
    public async Task CreateAsync_InitializesEmptyCollections()
    {
        var persona = await _service.CreateAsync("Sarah", null);

        Assert.Empty(persona.Memories);
        Assert.Empty(persona.Snapshots);
        Assert.Empty(persona.Personality.Traits);
        Assert.Empty(persona.Timeline.KeyEvents);
    }

    // ================================================================
    // AddMemoryAsync
    // ================================================================

    [Fact]
    public async Task AddMemoryAsync_SetsStateProvisional()
    {
        var personaId = Guid.NewGuid();

        _storeMock.Setup(store => store.GetAsync(personaId, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new CanonicalPersona { Id = personaId, Name = "Sarah" });

        var memory = await _service.AddMemoryAsync(personaId, "She loved hiking.", MemoryType.Behavioral, userAsserted: true);

        Assert.Equal(MemoryState.Provisional, memory.State);
    }

    [Fact]
    public async Task AddMemoryAsync_SetsSourceUserFragment_WhenUserAsserted()
    {
        var personaId = Guid.NewGuid();

        _storeMock.Setup(store => store.GetAsync(personaId, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new CanonicalPersona { Id = personaId, Name = "Sarah" });

        var memory = await _service.AddMemoryAsync(personaId, "She loved hiking.", MemoryType.Behavioral, userAsserted: true);

        Assert.Equal(MemorySource.UserFragment, memory.Source);
        Assert.True(memory.UserAsserted);
        Assert.False(memory.AIInferred);
    }

    [Fact]
    public async Task AddMemoryAsync_SetsSourceAiInferred_WhenNotUserAsserted()
    {
        var personaId = Guid.NewGuid();

        _storeMock.Setup(store => store.GetAsync(personaId, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new CanonicalPersona { Id = personaId, Name = "Sarah" });

        var memory = await _service.AddMemoryAsync(personaId, "She seemed anxious.", MemoryType.Emotional, userAsserted: false);

        Assert.Equal(MemorySource.AIInferred, memory.Source);
        Assert.False(memory.UserAsserted);
        Assert.True(memory.AIInferred);
    }

    [Fact]
    public async Task AddMemoryAsync_SetsPersonaIdAndContent()
    {
        var personaId = Guid.NewGuid();

        _storeMock.Setup(store => store.GetAsync(personaId, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new CanonicalPersona { Id = personaId, Name = "Sarah" });

        var memory = await _service.AddMemoryAsync(personaId, "She loved hiking.", MemoryType.Behavioral, userAsserted: true);

        Assert.Equal(personaId,           memory.PersonaId);
        Assert.Equal("She loved hiking.", memory.Content);
    }

    [Fact]
    public async Task AddMemoryAsync_Throws_WhenPersonaNotFound()
    {
        var personaId = Guid.NewGuid();

        _storeMock.Setup(store => store.GetAsync(personaId, It.IsAny<CancellationToken>()))
                  .ReturnsAsync((CanonicalPersona?)null);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.AddMemoryAsync(personaId, "content", MemoryType.Narrative, userAsserted: true));
    }

    // ================================================================
    // CreateSnapshotAsync
    // ================================================================

    [Fact]
    public async Task CreateSnapshotAsync_IncludesAllMemoryIds()
    {
        var personaId = Guid.NewGuid();
        var memId1    = Guid.NewGuid();
        var memId2    = Guid.NewGuid();

        _storeMock.Setup(store => store.GetAsync(personaId, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new CanonicalPersona { Id = personaId, Name = "Sarah" });

        _storeMock.Setup(store => store.GetMemoriesAsync(personaId, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new List<PersonaMemory>
                                {
                                        new() { Id = memId1, PersonaId = personaId }
                                      , new() { Id = memId2, PersonaId = personaId }
                                });

        var snapshot = await _service.CreateSnapshotAsync(personaId, "Week 1", null);

        Assert.Contains(memId1, snapshot.IncludedMemories);
        Assert.Contains(memId2, snapshot.IncludedMemories);
        Assert.Equal(2, snapshot.IncludedMemories.Count);
    }

    [Fact]
    public async Task CreateSnapshotAsync_SetsPersonaIdAndName()
    {
        var personaId = Guid.NewGuid();

        _storeMock.Setup(store => store.GetAsync(personaId, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new CanonicalPersona { Id = personaId, Name = "Sarah" });

        _storeMock.Setup(store => store.GetMemoriesAsync(personaId, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new List<PersonaMemory>());

        var snapshot = await _service.CreateSnapshotAsync(personaId, "Week 1", "Initial snapshot");

        Assert.Equal(personaId,           snapshot.PersonaId);
        Assert.Equal("Week 1",            snapshot.Name);
        Assert.Equal("Initial snapshot",  snapshot.Notes);
    }

    [Fact]
    public async Task CreateSnapshotAsync_Throws_WhenPersonaNotFound()
    {
        var personaId = Guid.NewGuid();

        _storeMock.Setup(store => store.GetAsync(personaId, It.IsAny<CancellationToken>()))
                  .ReturnsAsync((CanonicalPersona?)null);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.CreateSnapshotAsync(personaId, "snapshot", null));
    }
}
