using Moq;
using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Domains.DailyRecord;
using CognitivePlatform.Api.Domains.Journal;
using CognitivePlatform.Api.Domains.Tasks;
using CognitivePlatform.Api.Integrations.Embeddings;
using CognitivePlatform.Api.Services;
using Microsoft.Extensions.Logging;

namespace CognitivePlatform.Tests;

public class EmbeddingBackfillServiceTests
{
    private readonly Mock<IObjectStore>                        _storeMock        = new();
    private readonly Mock<IEmbeddingService>                   _embeddingMock    = new();
    private readonly Mock<IVectorStore>                        _vectorStoreMock  = new();
    private readonly Mock<ILogger<EmbeddingBackfillService>>   _loggerMock       = new();
    private readonly EmbeddingBackfillService                  _service;

    public EmbeddingBackfillServiceTests()
    {
        _vectorStoreMock.Setup(store => store.SaveAsync(It.IsAny<VectorEntry>()
                                                       , It.IsAny<CancellationToken>()))
                        .Returns(Task.CompletedTask);

        _service = new EmbeddingBackfillService(_storeMock.Object
                                               , _embeddingMock.Object
                                               , _vectorStoreMock.Object
                                               , _loggerMock.Object);
    }

    [Fact]
    public async Task ExecuteAsync_SkipsBackfill_WhenEmbeddingServiceIsUnavailable()
    {
        _embeddingMock.Setup(service => service.IsAvailable).Returns(false);

        await _service.RunBackfillAsync(CancellationToken.None);

        _storeMock.Verify(store => store.List<JournalEntry>(It.IsAny<string?>()
                                                           , It.IsAny<DateTimeOffset?>()
                                                           , It.IsAny<DateTimeOffset?>())
                        , Times.Never);
        _embeddingMock.Verify(service => service.EmbedAsync(It.IsAny<string>()
                                                           , It.IsAny<CancellationToken>())
                            , Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_EmbedsJournalEntries_WhenEmbeddingServiceIsAvailable()
    {
        var entryId  = Guid.NewGuid().ToString("N");
        var entry    = new JournalEntry { Id = entryId };
        var revision = new JournalRevision
                       {
                               RevisionId = Guid.NewGuid().ToString("N")
                             , EntryId    = entryId
                             , Text       = "Today was a great day."
                             , CreatedUtc = DateTimeOffset.UtcNow
                       };

        _embeddingMock.Setup(service => service.IsAvailable).Returns(true);
        _embeddingMock.Setup(service => service.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                      .ReturnsAsync(new float[] { 0.1f, 0.2f, 0.3f });

        _storeMock.Setup(store => store.List<JournalEntry>(null, null, null))
                  .Returns(new List<JournalEntry> { entry });
        _storeMock.Setup(store => store.List<JournalRevision>(null, null, null))
                  .Returns(new List<JournalRevision> { revision });
        _storeMock.Setup(store => store.List<TaskItem>(null, null, null))
                  .Returns(new List<TaskItem>());
        _storeMock.Setup(store => store.List<DailyRecord>(null, null, null))
                  .Returns(new List<DailyRecord>());

        await _service.RunBackfillAsync(CancellationToken.None);

        _embeddingMock.Verify(service => service.EmbedAsync("Today was a great day."
                                                           , It.IsAny<CancellationToken>())
                            , Times.Once);
        _vectorStoreMock.Verify(store => store.SaveAsync(It.Is<VectorEntry>(entry => entry.Domain      == "journal"
                                                                                  && entry.ReferenceId == entryId)
                                                       , It.IsAny<CancellationToken>())
                              , Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_EmbedsTaskItems_WhenEmbeddingServiceIsAvailable()
    {
        var taskId = Guid.NewGuid().ToString("N");
        var task   = new TaskItem
                     {
                             Id               = taskId
                           , ShortDescription = "Finish the report"
                           , Details          = "Focus on the executive summary"
                     };

        _embeddingMock.Setup(service => service.IsAvailable).Returns(true);
        _embeddingMock.Setup(service => service.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                      .ReturnsAsync(new float[] { 0.5f, 0.6f });

        _storeMock.Setup(store => store.List<JournalEntry>(null, null, null))
                  .Returns(new List<JournalEntry>());
        _storeMock.Setup(store => store.List<JournalRevision>(null, null, null))
                  .Returns(new List<JournalRevision>());
        _storeMock.Setup(store => store.List<TaskItem>(null, null, null))
                  .Returns(new List<TaskItem> { task });
        _storeMock.Setup(store => store.List<DailyRecord>(null, null, null))
                  .Returns(new List<DailyRecord>());

        await _service.RunBackfillAsync(CancellationToken.None);

        _embeddingMock.Verify(service => service.EmbedAsync("Finish the report Focus on the executive summary"
                                                           , It.IsAny<CancellationToken>())
                            , Times.Once);
        _vectorStoreMock.Verify(store => store.SaveAsync(It.Is<VectorEntry>(entry => entry.Domain      == "task"
                                                                                  && entry.ReferenceId == taskId)
                                                       , It.IsAny<CancellationToken>())
                              , Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_SkipsSoftDeletedEntries_DuringBackfill()
    {
        var deletedEntry = new JournalEntry
                           {
                                   Id         = Guid.NewGuid().ToString("N")
                                 , DeletedUtc = DateTimeOffset.UtcNow
                           };

        _embeddingMock.Setup(service => service.IsAvailable).Returns(true);

        _storeMock.Setup(store => store.List<JournalEntry>(null, null, null))
                  .Returns(new List<JournalEntry> { deletedEntry });
        _storeMock.Setup(store => store.List<JournalRevision>(null, null, null))
                  .Returns(new List<JournalRevision>());
        _storeMock.Setup(store => store.List<TaskItem>(null, null, null))
                  .Returns(new List<TaskItem>());
        _storeMock.Setup(store => store.List<DailyRecord>(null, null, null))
                  .Returns(new List<DailyRecord>());

        await _service.RunBackfillAsync(CancellationToken.None);

        _embeddingMock.Verify(service => service.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
