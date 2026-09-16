using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Domains.Journal;
using CognitivePlatform.Api.Domains.Journal.Import.Ttr;
using CognitivePlatform.Api.Integrations.Embeddings;
using Microsoft.Data.Sqlite;
using Moq;

namespace CognitivePlatform.Tests;

public sealed class TtrEmbeddingReindexServiceTests : IDisposable
{
    private readonly SqliteConnection  _connection;
    private readonly SqliteObjectStore _store;

    public TtrEmbeddingReindexServiceTests()
    {
        var connectionString = $"Data Source={Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        _connection = new SqliteConnection(connectionString);
        _connection.Open();
        _store = new SqliteObjectStore(connectionString);
    }

    [Fact]
    public async Task ReindexAsync_VerifiedBatch_EmbedsOnlyImportedItemsAndRecordsCompletion()
    {
        var batch = new TtrImportBatch
                    {
                        Id                 = "batch"
                      , Status             = "Completed"
                      , VerificationPassed = true
                      , TargetPartition    = "personal"
                    };
        var item = new TtrImportItem
                   {
                       Id            = "item"
                     , BatchId       = batch.Id
                     , SourceEntryId = 1
                     , EntryId       = Guid.NewGuid().ToString()
                     , RevisionId    = Guid.NewGuid().ToString()
                     , Status        = "Imported"
                   };
        var revision = new JournalRevision
                       {
                           RevisionId = item.RevisionId
                         , EntryId    = item.EntryId
                         , CreatedUtc = DateTimeOffset.UtcNow
                         , Text       = "historical text"
                         , State      = JournalEntryState.Committed
                       };
        await _store.Save(batch, id: batch.Id);
        await _store.Save(item, id: item.Id);
        await _store.SaveHistorical(revision, revision.CreatedUtc, batch.TargetPartition, revision.RevisionId);
        var embeddings = new Mock<IEmbeddingService>();
        embeddings.SetupGet(service => service.IsAvailable).Returns(true);
        embeddings.Setup(service => service.EmbedAsync("historical text", It.IsAny<CancellationToken>()))
                  .ReturnsAsync([0.1f, 0.2f]);
        var vectors = new Mock<IVectorStore>();
        var service = new TtrEmbeddingReindexService(_store, embeddings.Object, vectors.Object);

        var result = await service.ReindexAsync(batch.Id);

        Assert.Equal("Completed", result.Status);
        Assert.Equal(1, result.EmbeddedCount);
        Assert.Equal(0, result.FailedCount);
        vectors.Verify(store => store.SaveAsync(It.Is<VectorEntry>(entry => entry.ReferenceId == item.EntryId
                                                                         && entry.Text == "historical text")
                                              , It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReindexAsync_UnverifiedBatch_BlocksBeforeEmbedding()
    {
        var batch = new TtrImportBatch { Id = "batch", Status = "Failed", VerificationPassed = false };
        await _store.Save(batch, id: batch.Id);
        var service = new TtrEmbeddingReindexService(_store, Mock.Of<IEmbeddingService>(), Mock.Of<IVectorStore>());

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ReindexAsync(batch.Id));
    }

    public void Dispose() => _connection.Dispose();
}
