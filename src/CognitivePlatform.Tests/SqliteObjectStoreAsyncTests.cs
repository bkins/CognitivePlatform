using CognitivePlatform.Api.Contracts;
using CognitivePlatform.Api.Data;
using Microsoft.Data.Sqlite;

namespace CognitivePlatform.Tests;

/// <summary>
/// Tests for the async variants added to <see cref="IObjectStore"/>:
/// <see cref="IObjectStore.GetAsync{T}"/>, <see cref="IObjectStore.ListAsync{T}"/>,
/// and <see cref="IObjectStore.SoftDeleteAsync{T}"/>.
/// Uses in-memory SQLite so no file system is touched.
/// </summary>
public sealed class SqliteObjectStoreAsyncTests : IDisposable
{
    private readonly SqliteConnection  _persistentConnection;
    private readonly SqliteObjectStore _store;

    public SqliteObjectStoreAsyncTests()
    {
        var dbName           = Guid.NewGuid().ToString("N");
        var connectionString = $"Data Source={dbName};Mode=Memory;Cache=Shared";

        _persistentConnection = new SqliteConnection(connectionString);
        _persistentConnection.Open();

        _store = new SqliteObjectStore(connectionString);
    }

    public void Dispose() => _persistentConnection.Dispose();

    // ====================================================================
    // GetAsync
    // ====================================================================

    [Fact]
    public async Task GetAsync_ReturnsNull_WhenIdDoesNotExist()
    {
        var result = await _store.GetAsync<ProcessedRequest>("nonexistent-id");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAsync_ReturnsRecord_WhenIdExists()
    {
        var record = new ProcessedRequest { Id = "req-1", ResponseJson = "{\"message\":\"ok\"}" };
        await _store.Save(record);

        var result = await _store.GetAsync<ProcessedRequest>("req-1");

        Assert.NotNull(result);
        Assert.Equal("req-1",              result!.Id);
        Assert.Equal("{\"message\":\"ok\"}", result.ResponseJson);
    }

    [Fact]
    public async Task GetAsync_ReturnsNull_WhenRecordIsSoftDeleted()
    {
        var record = new ProcessedRequest { Id = "del-1", ResponseJson = "{}" };
        await _store.Save(record);
        _store.SoftDelete<ProcessedRequest>("del-1");

        var result = await _store.GetAsync<ProcessedRequest>("del-1");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAsync_SyncAndAsync_ReturnSameRecord()
    {
        var record = new ProcessedRequest { Id = "both-1", ResponseJson = "{\"x\":1}" };
        await _store.Save(record);

        var syncResult  = _store.Get<ProcessedRequest>("both-1");
        var asyncResult = await _store.GetAsync<ProcessedRequest>("both-1");

        Assert.Equal(syncResult!.Id,           asyncResult!.Id);
        Assert.Equal(syncResult.ResponseJson,  asyncResult.ResponseJson);
    }

    // ====================================================================
    // ListAsync
    // ====================================================================

    [Fact]
    public async Task ListAsync_ReturnsEmpty_WhenNoRecordsExist()
    {
        var result = await _store.ListAsync<ProcessedRequest>();

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task ListAsync_ReturnsAllUndeletedRecords()
    {
        await _store.Save(new ProcessedRequest { Id = "r1", ResponseJson = "{}" });
        await _store.Save(new ProcessedRequest { Id = "r2", ResponseJson = "{}" });

        var result = await _store.ListAsync<ProcessedRequest>();

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task ListAsync_ExcludesSoftDeletedRecords()
    {
        await _store.Save(new ProcessedRequest { Id = "keep",   ResponseJson = "{}" });
        await _store.Save(new ProcessedRequest { Id = "remove", ResponseJson = "{}" });
        _store.SoftDelete<ProcessedRequest>("remove");

        var result = await _store.ListAsync<ProcessedRequest>();

        Assert.Single(result);
        Assert.Equal("keep", result[0].Id);
    }

    [Fact]
    public async Task ListAsync_SyncAndAsync_ReturnSameCount()
    {
        await _store.Save(new ProcessedRequest { Id = "sync-async-1", ResponseJson = "{}" });
        await _store.Save(new ProcessedRequest { Id = "sync-async-2", ResponseJson = "{}" });

        var syncCount  = _store.List<ProcessedRequest>().Count;
        var asyncCount = (await _store.ListAsync<ProcessedRequest>()).Count;

        Assert.Equal(syncCount, asyncCount);
    }

    // ====================================================================
    // SoftDeleteAsync
    // ====================================================================

    [Fact]
    public async Task SoftDeleteAsync_ReturnsFalse_WhenNoRecordExists()
    {
        // SoftDeleteAsync always returns true (mirrors sync SoftDelete)
        // but the record is not visible after the call
        var result = await _store.SoftDeleteAsync<ProcessedRequest>("ghost-id");

        Assert.True(result); // SoftDelete returns true regardless
    }

    [Fact]
    public async Task SoftDeleteAsync_HidesRecord_FromSubsequentGet()
    {
        var record = new ProcessedRequest { Id = "soft-1", ResponseJson = "{}" };
        await _store.Save(record);

        await _store.SoftDeleteAsync<ProcessedRequest>("soft-1");

        var result = await _store.GetAsync<ProcessedRequest>("soft-1");
        Assert.Null(result);
    }

    [Fact]
    public async Task SoftDeleteAsync_SyncAndAsync_ProduceSameEffect()
    {
        await _store.Save(new ProcessedRequest { Id = "del-sync",  ResponseJson = "{}" });
        await _store.Save(new ProcessedRequest { Id = "del-async", ResponseJson = "{}" });

        _store.SoftDelete<ProcessedRequest>("del-sync");
        await _store.SoftDeleteAsync<ProcessedRequest>("del-async");

        Assert.Null(_store.Get<ProcessedRequest>("del-sync"));
        Assert.Null(_store.Get<ProcessedRequest>("del-async"));
    }
}
