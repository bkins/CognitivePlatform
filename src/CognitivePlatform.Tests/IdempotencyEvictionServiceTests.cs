using CognitivePlatform.Api.Contracts;
using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Services;
using CognitivePlatform.Api.Telemetry;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace CognitivePlatform.Tests;

/// <summary>
/// Tests for <see cref="SqliteObjectStore.DeleteOlderThan{T}"/> and the
/// <see cref="IdempotencyEvictionService"/> hosted service lifecycle.
/// Uses an in-memory SQLite database so no file system is touched.
/// </summary>
public sealed class IdempotencyEvictionServiceTests : IDisposable
{
    private readonly SqliteConnection  _persistentConnection;
    private readonly SqliteObjectStore _store;

    public IdempotencyEvictionServiceTests()
    {
        var dbName           = Guid.NewGuid().ToString("N");
        var connectionString = $"Data Source={dbName};Mode=Memory;Cache=Shared";

        _persistentConnection = new SqliteConnection(connectionString);
        _persistentConnection.Open();

        _store = new SqliteObjectStore(connectionString);
    }

    public void Dispose() => _persistentConnection.Dispose();

    // ====================================================================
    // SqliteObjectStore.DeleteOlderThan<T>
    // ====================================================================

    [Fact]
    public async Task DeleteOlderThan_DeletesAllRecords_WhenWindowIsZero()
    {
        await _store.Save(new ProcessedRequest { Id = "old-1", ResponseJson = "{}", CreatedUtc = DateTimeOffset.UtcNow });
        await _store.Save(new ProcessedRequest { Id = "old-2", ResponseJson = "{}", CreatedUtc = DateTimeOffset.UtcNow });

        var before = _store.List<ProcessedRequest>();
        Assert.Equal(2, before.Count);

        var deleted = _store.DeleteOlderThan<ProcessedRequest>(TimeSpan.Zero);

        Assert.Equal(2, deleted);
        Assert.Empty(_store.List<ProcessedRequest>());
    }

    [Fact]
    public async Task DeleteOlderThan_LeavesRecentRecordsUntouched()
    {
        await _store.Save(new ProcessedRequest { Id = "recent", ResponseJson = "{}", CreatedUtc = DateTimeOffset.UtcNow });

        var deleted = _store.DeleteOlderThan<ProcessedRequest>(TimeSpan.FromDays(30));

        Assert.Equal(0, deleted);
        Assert.Single(_store.List<ProcessedRequest>());
    }

    [Fact]
    public void DeleteOlderThan_ReturnsZero_WhenStoreIsEmpty()
    {
        var deleted = _store.DeleteOlderThan<ProcessedRequest>(TimeSpan.FromDays(7));

        Assert.Equal(0, deleted);
    }

    [Fact]
    public async Task DeleteOlderThan_IsTypeSafe_DoesNotDeleteOtherTypes()
    {
        await _store.Save(new ProcessedRequest { Id = "req", ResponseJson = "{}" });

        // DeleteOlderThan for a different type should not touch ProcessedRequest rows
        var deleted = _store.DeleteOlderThan<TelemetryRecord>(TimeSpan.Zero);

        Assert.Equal(0, deleted);
        Assert.Single(_store.List<ProcessedRequest>());
    }

    // ====================================================================
    // IdempotencyEvictionService — hosted service lifecycle smoke test
    // ====================================================================

    [Fact]
    public async Task Service_StartsAndStops_WithoutThrowing()
    {
        var service = new IdempotencyEvictionService(_store
                                                   , NullLogger<IdempotencyEvictionService>.Instance);

        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);
        service.Dispose();
    }
}
