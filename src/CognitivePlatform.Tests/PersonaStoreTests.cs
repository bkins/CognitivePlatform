using Moq;
using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Domains.Personas;
using CognitivePlatform.Api.Domains.Personas.Models;

namespace CognitivePlatform.Tests;

public class PersonaStoreTests
{
    private readonly Mock<IObjectStore> _storeMock = new();
    private readonly PersonaStore        _personaStore;

    public PersonaStoreTests()
    {
        _storeMock.Setup(store => store.Save(It.IsAny<CanonicalPersona>()
                                           , It.IsAny<string?>()
                                           , It.IsAny<string?>()))
                  .ReturnsAsync(string.Empty);

        _storeMock.Setup(store => store.Save(It.IsAny<PersonaMemory>()
                                           , It.IsAny<string?>()
                                           , It.IsAny<string?>()))
                  .ReturnsAsync(string.Empty);

        _storeMock.Setup(store => store.Save(It.IsAny<MemorySnapshot>()
                                           , It.IsAny<string?>()
                                           , It.IsAny<string?>()))
                  .ReturnsAsync(string.Empty);

        _personaStore = new PersonaStore(_storeMock.Object);
    }

    // ================================================================
    // SaveAsync
    // ================================================================

    [Fact]
    public async Task SaveAsync_UsesPersonaIdAsPartitionKeyAndId()
    {
        var persona = new CanonicalPersona
                      {
                              Id   = Guid.NewGuid()
                            , Name = "Sarah"
                      };

        await _personaStore.SaveAsync(persona);

        _storeMock.Verify(store => store.Save(persona
                                            , persona.Id.ToString()
                                            , persona.Id.ToString())
                        , Times.Once);
    }

    // ================================================================
    // GetAsync
    // ================================================================

    [Fact]
    public async Task GetAsync_ReturnsNull_WhenPersonaNotFound()
    {
        var personaId = Guid.NewGuid();

        _storeMock.Setup(store => store.Get<CanonicalPersona>(personaId.ToString(), personaId.ToString()))
                  .Returns((CanonicalPersona?)null);

        var result = await _personaStore.GetAsync(personaId);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAsync_ReturnsNull_WhenPersonaIsDeleted()
    {
        var personaId = Guid.NewGuid();
        var deleted   = new CanonicalPersona { Id = personaId, IsDeleted = true };

        _storeMock.Setup(store => store.Get<CanonicalPersona>(personaId.ToString(), personaId.ToString()))
                  .Returns(deleted);

        var result = await _personaStore.GetAsync(personaId);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAsync_ReturnsPersona_WhenFound()
    {
        var personaId = Guid.NewGuid();
        var persona   = new CanonicalPersona { Id = personaId, Name = "Sarah", IsDeleted = false };

        _storeMock.Setup(store => store.Get<CanonicalPersona>(personaId.ToString(), personaId.ToString()))
                  .Returns(persona);

        var result = await _personaStore.GetAsync(personaId);

        Assert.NotNull(result);
        Assert.Equal("Sarah", result.Name);
    }

    // ================================================================
    // SoftDeleteAsync
    // ================================================================

    [Fact]
    public async Task SoftDeleteAsync_SetsIsDeletedTrue_AndSaves()
    {
        var personaId = Guid.NewGuid();
        var persona   = new CanonicalPersona { Id = personaId, IsDeleted = false };

        _storeMock.Setup(store => store.Get<CanonicalPersona>(personaId.ToString(), personaId.ToString()))
                  .Returns(persona);

        await _personaStore.SoftDeleteAsync(personaId);

        _storeMock.Verify(store => store.Save(
                              It.Is<CanonicalPersona>(savedPersona => savedPersona.IsDeleted && savedPersona.DeletedUtc.HasValue)
                            , personaId.ToString()
                            , personaId.ToString())
                        , Times.Once);
    }

    [Fact]
    public async Task SoftDeleteAsync_DoesNothing_WhenPersonaNotFound()
    {
        var personaId = Guid.NewGuid();

        _storeMock.Setup(store => store.Get<CanonicalPersona>(personaId.ToString(), personaId.ToString()))
                  .Returns((CanonicalPersona?)null);

        await _personaStore.SoftDeleteAsync(personaId);

        _storeMock.Verify(store => store.Save(It.IsAny<CanonicalPersona>()
                                            , It.IsAny<string?>()
                                            , It.IsAny<string?>())
                        , Times.Never);
    }

    // ================================================================
    // AddMemoryAsync
    // ================================================================

    [Fact]
    public async Task AddMemoryAsync_UsesMemoryPartitionKey()
    {
        var personaId = Guid.NewGuid();
        var memoryId  = Guid.NewGuid();
        var memory    = new PersonaMemory { Id = memoryId, PersonaId = personaId };

        await _personaStore.AddMemoryAsync(memory);

        _storeMock.Verify(store => store.Save(memory
                                            , $"memory:{personaId}"
                                            , memoryId.ToString())
                        , Times.Once);
    }

    // ================================================================
    // SaveSnapshotAsync
    // ================================================================

    [Fact]
    public async Task SaveSnapshotAsync_UsesSnapshotPartitionKey()
    {
        var personaId  = Guid.NewGuid();
        var snapshotId = Guid.NewGuid();
        var snapshot   = new MemorySnapshot { Id = snapshotId, PersonaId = personaId };

        await _personaStore.SaveSnapshotAsync(snapshot);

        _storeMock.Verify(store => store.Save(snapshot
                                            , $"snapshot:{personaId}"
                                            , snapshotId.ToString())
                        , Times.Once);
    }
}
