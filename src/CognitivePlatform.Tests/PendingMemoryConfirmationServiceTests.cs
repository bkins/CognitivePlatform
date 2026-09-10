using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Domains.Personas;
using CognitivePlatform.Api.Domains.Personas.Models;
using Moq;

namespace CognitivePlatform.Tests;

public class PendingMemoryConfirmationServiceTests
{
    private const string PartitionKey = "pending-memory-confirmation";

    private readonly Mock<IObjectStore> _storeMock = new();
    private readonly PendingMemoryConfirmationService _service;

    public PendingMemoryConfirmationServiceTests()
    {
        _storeMock.Setup(store => store.Save(It.IsAny<PendingMemoryConfirmation>()
                                           , PartitionKey
                                           , It.IsAny<string?>()))
                  .ReturnsAsync(string.Empty);

        _service = new PendingMemoryConfirmationService(_storeMock.Object);
    }

    [Fact]
    public async Task EnqueueAsync_PersistsSourceLinkedRecord_WhenNoRecordExists()
    {
        var memory = new PersonaMemory { Id = Guid.NewGuid(), PersonaId = Guid.NewGuid() };
        _storeMock.Setup(store => store.Get<PendingMemoryConfirmation>(memory.Id.ToString(), PartitionKey))
                  .Returns((PendingMemoryConfirmation?)null);

        await _service.EnqueueAsync("conversation-1", memory);

        _storeMock.Verify(store => store.Save(It.Is<PendingMemoryConfirmation>(confirmation =>
                                                   confirmation.Id == memory.Id
                                                && confirmation.PersonaMemoryId == memory.Id
                                                && confirmation.PersonaId == memory.PersonaId
                                                && confirmation.ConversationId == "conversation-1"
                                                && !confirmation.IsResolved)
                                            , PartitionKey
                                            , memory.Id.ToString())
                        , Times.Once);
    }

    [Fact]
    public async Task EnqueueAsync_DoesNotDuplicateExistingRecord()
    {
        var memory = new PersonaMemory { Id = Guid.NewGuid() };
        _storeMock.Setup(store => store.Get<PendingMemoryConfirmation>(memory.Id.ToString(), PartitionKey))
                  .Returns(new PendingMemoryConfirmation { Id = memory.Id });

        await _service.EnqueueAsync("conversation-1", memory);

        _storeMock.Verify(store => store.Save(It.IsAny<PendingMemoryConfirmation>(), PartitionKey, It.IsAny<string?>())
                        , Times.Never);
    }

    [Fact]
    public async Task ResolveAsync_MarksRecordResolved_WithoutDeletingAuditRecord()
    {
        var confirmation = new PendingMemoryConfirmation { Id = Guid.NewGuid(), IsResolved = false };
        _storeMock.Setup(store => store.Get<PendingMemoryConfirmation>(confirmation.Id.ToString(), PartitionKey))
                  .Returns(confirmation);

        await _service.ResolveAsync(confirmation.Id, "confirmed");

        Assert.True(confirmation.IsResolved);
        Assert.Equal("confirmed", confirmation.ResolutionReason);
        Assert.NotNull(confirmation.ResolvedUtc);
        _storeMock.Verify(store => store.Save(confirmation, PartitionKey, confirmation.Id.ToString()), Times.Once);
    }

    [Fact]
    public async Task GetSummaryAsync_CountsOnlyUnresolvedRecords()
    {
        _storeMock.Setup(store => store.List<PendingMemoryConfirmation>(PartitionKey, null, null))
                  .Returns(
                  [
                      new PendingMemoryConfirmation { Id = Guid.NewGuid(), IsResolved = false }
                    , new PendingMemoryConfirmation { Id = Guid.NewGuid(), IsResolved = false }
                    , new PendingMemoryConfirmation { Id = Guid.NewGuid(), IsResolved = true }
                  ]);

        var summary = await _service.GetSummaryAsync();

        Assert.Equal(2, summary.PendingCount);
    }
}
