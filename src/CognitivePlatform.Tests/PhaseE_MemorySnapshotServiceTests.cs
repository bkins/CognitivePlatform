using Moq;
using CognitivePlatform.Api.Domains.Personas;
using CognitivePlatform.Api.Domains.Personas.Models;

namespace CognitivePlatform.Tests;

public class PhaseE_MemorySnapshotServiceTests
{
    private readonly Mock<IPersonaStore>          _personaStoreMock  = new();
    private readonly Mock<IPersonaSessionManager> _sessionManagerMock = new();
    private readonly MemorySnapshotService        _service;

    public PhaseE_MemorySnapshotServiceTests()
    {
        _service = new MemorySnapshotService(_personaStoreMock.Object
                                           , _sessionManagerMock.Object);
    }

    [Fact]
    public async Task CreateSnapshot_AssignsNonEmptyId()
    {
        var personaId = Guid.NewGuid();
        var persona   = new CanonicalPersona { Id = personaId, Name = "Sarah" };

        _personaStoreMock
            .Setup(store => store.GetAsync(personaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(persona);

        _personaStoreMock
            .Setup(store => store.GetMemoriesAsync(personaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _personaStoreMock
            .Setup(store => store.SaveSnapshotAsync(It.IsAny<MemorySnapshot>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var snapshot = await _service.CreateSnapshotAsync(personaId, "romanticized", "a note");

        Assert.NotEqual(Guid.Empty, snapshot.Id);
    }

    [Fact]
    public async Task CreateSnapshot_CapturesCurrentMemoryIds()
    {
        var personaId = Guid.NewGuid();
        var persona   = new CanonicalPersona { Id = personaId, Name = "Sarah" };
        var memoryOne = new PersonaMemory { Id = Guid.NewGuid(), Content = "Loved hiking." };
        var memoryTwo = new PersonaMemory { Id = Guid.NewGuid(), Content = "Always laughed." };

        _personaStoreMock
            .Setup(store => store.GetAsync(personaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(persona);

        _personaStoreMock
            .Setup(store => store.GetMemoriesAsync(personaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([ memoryOne, memoryTwo ]);

        _personaStoreMock
            .Setup(store => store.SaveSnapshotAsync(It.IsAny<MemorySnapshot>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var snapshot = await _service.CreateSnapshotAsync(personaId, "test", "");

        Assert.Contains(memoryOne.Id, snapshot.IncludedMemories);
        Assert.Contains(memoryTwo.Id, snapshot.IncludedMemories);
    }

    [Fact]
    public async Task CreateSnapshot_ThrowsInvalidOperation_WhenPersonaNotFound()
    {
        var personaId = Guid.NewGuid();

        _personaStoreMock
            .Setup(store => store.GetAsync(personaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CanonicalPersona?)null);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.CreateSnapshotAsync(personaId, "name", "notes"));
    }

    [Fact]
    public async Task LoadSnapshot_SetsSnapshotOverrideInSessionManager()
    {
        var personaId  = Guid.NewGuid();
        var snapshotId = Guid.NewGuid();
        var memoryId   = Guid.NewGuid();
        var persona    = new CanonicalPersona { Id = personaId, Name = "Sarah" };
        var snapshot   = new MemorySnapshot { Id = snapshotId, PersonaId = personaId, IncludedMemories = [ memoryId ] };
        var memory     = new PersonaMemory { Id = memoryId, Content = "She loved jazz." };

        _personaStoreMock
            .Setup(store => store.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([ persona ]);

        _personaStoreMock
            .Setup(store => store.GetSnapshotsAsync(personaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([ snapshot ]);

        _personaStoreMock
            .Setup(store => store.GetMemoriesAsync(personaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([ memory ]);

        await _service.LoadSnapshotAsync(snapshotId, "conv-123");

        _sessionManagerMock.Verify(manager => manager.SetSnapshotOverride(
                                       "conv-123"
                                     , It.Is<PersonaSnapshotOverride>(
                                           snapshotOverride => snapshotOverride.SnapshotId == snapshotId
                                                            && snapshotOverride.Memories.Count == 1))
                                 , Times.Once);
    }

    [Fact]
    public async Task UnloadSnapshot_ClearsSnapshotOverride()
    {
        await _service.UnloadSnapshotAsync("conv-999");

        _sessionManagerMock.Verify(manager => manager.ClearSnapshotOverride("conv-999"), Times.Once);
    }

    [Fact]
    public async Task GetSnapshots_DelegatesToPersonaStore()
    {
        var personaId = Guid.NewGuid();
        var snapshot  = new MemorySnapshot { Id = Guid.NewGuid(), PersonaId = personaId };

        _personaStoreMock
            .Setup(store => store.GetSnapshotsAsync(personaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([ snapshot ]);

        var results = await _service.GetSnapshotsAsync(personaId);

        Assert.Single(results);
        Assert.Equal(snapshot.Id, results[0].Id);
    }
}
