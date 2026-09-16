using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Domains.Journal;
using CognitivePlatform.Api.Domains.Journal.Import.Ttr;
using CognitivePlatform.Api.Domains.Media;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace CognitivePlatform.Tests;

public sealed class TtrHistoricalWriteContractTests : IDisposable
{
    private readonly SqliteConnection  _connection;
    private readonly SqliteObjectStore _store;

    public TtrHistoricalWriteContractTests()
    {
        var databaseName    = Guid.NewGuid().ToString("N");
        var connectionString = $"Data Source={databaseName};Mode=Memory;Cache=Shared";
        _connection = new SqliteConnection(connectionString);
        _connection.Open();
        _store = new SqliteObjectStore(connectionString);
    }

    [Fact]
    public async Task SaveHistorical_UsesHistoricalMetadataAndPreservesItOnReplay()
    {
        var created = new DateTimeOffset(2021, 1, 2, 11, 4, 5, TimeSpan.Zero);
        var entry   = new JournalEntry { Id = Guid.NewGuid().ToString(), CreatedUtc = created };

        await _store.SaveHistorical(entry, created, "personal", entry.Id);
        await _store.SaveHistorical(entry, created.AddDays(1), "personal", entry.Id);

        Assert.Single(_store.List<JournalEntry>("personal", created.AddMinutes(-1), created.AddMinutes(1)));
        Assert.Empty(_store.List<JournalEntry>("personal", created.AddDays(1).AddMinutes(-1), created.AddDays(1).AddMinutes(1)));
    }

    [Fact]
    public async Task CreateAsync_MatchingReplay_IsIdempotentAndCollisionBlocks()
    {
        var writer  = new HistoricalJournalWriter(_store, _store);
        var request = CreateJournalRequest();

        var first  = await writer.CreateAsync(request);
        var replay = await writer.CreateAsync(request);

        Assert.False(first.AlreadyExists);
        Assert.True(replay.AlreadyExists);
        Assert.Single(_store.List<JournalEntry>("personal"));
        Assert.Single(_store.List<JournalRevision>("personal"));

        var changed = CreateJournalRequest(request.EntryId, request.RevisionId, "changed");
        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.CreateAsync(changed));
    }

    [Fact]
    public async Task CreateAsync_EntryPersistedBeforeInterruption_ResumesMissingRevision()
    {
        var writer  = new HistoricalJournalWriter(_store, _store);
        var request = CreateJournalRequest();
        await _store.SaveHistorical(new JournalEntry { Id = request.EntryId, CreatedUtc = request.CreatedUtc }
                                  , request.CreatedUtc
                                  , request.PartitionKey
                                  , request.EntryId);

        var result = await writer.CreateAsync(request);

        Assert.False(result.AlreadyExists);
        Assert.NotNull(_store.Get<JournalRevision>(request.RevisionId, request.PartitionKey));
        Assert.Single(_store.List<JournalEntry>(request.PartitionKey));
    }

    [Fact]
    public async Task AddImportedAttachmentAsync_UsesDeterministicIdTimestampAndCollisionSafePath()
    {
        var fileStorage = new Mock<IMediaFileStorage>();
        fileStorage.Setup(storage => storage.WriteAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
                   .Returns(Task.CompletedTask);
        var settings = Options.Create(new MediaAttachmentSettings { MediaRootPath = @"C:\TestMedia" });
        var service  = new MediaAttachmentService(_store
                                                , fileStorage.Object
                                                , settings
                                                , Mock.Of<ILogger<MediaAttachmentService>>());
        var created  = new DateTimeOffset(2021, 1, 2, 11, 4, 5, TimeSpan.Zero);
        var id       = Guid.NewGuid().ToString();

        await using var content = new MemoryStream([1, 2, 3]);
        var result = await service.AddImportedAttachmentAsync(id, "JournalEntry", "entry-1", "bad:name?.jpg", "image/jpeg", content, 3, created);

        Assert.Equal(id, result.Id);
        Assert.Equal(created, result.CreatedAt);
        Assert.Contains(id, result.StoragePath, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(":", Path.GetFileName(result.StoragePath));
        Assert.DoesNotContain("?", Path.GetFileName(result.StoragePath));
        Assert.Single(_store.List<MediaAttachment>(null, created.AddMinutes(-1), created.AddMinutes(1)));
    }

    public void Dispose() => _connection.Dispose();

    private static HistoricalJournalWriteRequest CreateJournalRequest(string? entryId = null, string? revisionId = null, string text = "# Imported")
    {
        return new HistoricalJournalWriteRequest
               {
                   EntryId     = entryId ?? Guid.NewGuid().ToString()
                 , RevisionId  = revisionId ?? Guid.NewGuid().ToString()
                 , CreatedUtc  = new DateTimeOffset(2021, 1, 2, 11, 4, 5, TimeSpan.Zero)
                 , PartitionKey = "personal"
                 , Text        = text
                 , Tags        = ["source:ttr"]
                 , Mood        = "Good 🙂"
                 , MediaPaths  = []
               };
    }
}
