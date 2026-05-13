using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Insights;
using CognitivePlatform.Api.Insights.Models;
using Microsoft.Data.Sqlite;

namespace CognitivePlatform.Tests;

/// <summary>
/// Tests for <see cref="ObjectStoreInsightHistoryStore"/> backed by an in-memory
/// SQLite database so no file system is touched.
/// </summary>
public sealed class ObjectStoreInsightHistoryStoreTests : IDisposable
{
    private readonly SqliteConnection            _persistentConnection;
    private readonly SqliteObjectStore           _objectStore;
    private readonly ObjectStoreInsightHistoryStore _store;

    public ObjectStoreInsightHistoryStoreTests()
    {
        // Each test instance gets a unique named in-memory DB.
        // A persistent connection is kept open so the shared in-memory database
        // survives across the per-operation connections that SqliteObjectStore opens.
        var dbName            = Guid.NewGuid().ToString("N");
        var connectionString  = $"Data Source={dbName};Mode=Memory;Cache=Shared";

        _persistentConnection = new SqliteConnection(connectionString);
        _persistentConnection.Open();

        _objectStore = new SqliteObjectStore(connectionString);
        _store       = new ObjectStoreInsightHistoryStore(_objectStore);
    }

    public void Dispose() => _persistentConnection.Dispose();

    private static Insight Make( string          deduplicationKey
                               , InsightCategory category = InsightCategory.Journal ) =>
        new()
        {
                Message          = $"Insight for {deduplicationKey}"
              , DeduplicationKey = deduplicationKey
              , Category         = category
        };

    // ====================================================================
    // WasRecentlyEmittedAsync
    // ====================================================================

    [Fact]
    public async Task WasRecentlyEmittedAsync_ReturnsFalse_WhenNoItemExists()
    {
        var found = await _store.WasRecentlyEmittedAsync("missing.key", TimeSpan.FromHours(1));

        Assert.False(found);
    }

    [Fact]
    public async Task WasRecentlyEmittedAsync_ReturnsTrue_WhenItemExistsWithinWindow()
    {
        await _store.RecordEmittedAsync(new[] { Make("journal.no-entry-today") });

        var found = await _store.WasRecentlyEmittedAsync("journal.no-entry-today", TimeSpan.FromHours(24));

        Assert.True(found);
    }

    [Fact]
    public async Task WasRecentlyEmittedAsync_ReturnsFalse_WhenItemExistsOutsideWindow()
    {
        await _store.RecordEmittedAsync(new[] { Make("journal.no-entry-today") });

        // Window of zero — anything older than "right now" is outside it.
        await Task.Delay(5);
        var found = await _store.WasRecentlyEmittedAsync("journal.no-entry-today", TimeSpan.Zero);

        Assert.False(found);
    }

    [Fact]
    public async Task WasRecentlyEmittedAsync_ReturnsFalse_ForDifferentKey()
    {
        await _store.RecordEmittedAsync(new[] { Make("journal.no-entry-today") });

        var found = await _store.WasRecentlyEmittedAsync("tasks.open-tasks", TimeSpan.FromHours(24));

        Assert.False(found);
    }

    // ====================================================================
    // RecordEmittedAsync
    // ====================================================================

    [Fact]
    public async Task RecordEmittedAsync_SkipsInsights_WithEmptyDeduplicationKey()
    {
        var noKey = new Insight { Message = "no key", DeduplicationKey = string.Empty };

        await _store.RecordEmittedAsync(new[] { noKey });

        var recent = await _store.GetRecentAsync(TimeSpan.FromHours(1));

        Assert.Empty(recent);
    }

    // ====================================================================
    // RecordOutcomeAsync
    // ====================================================================

    [Fact]
    public async Task RecordOutcomeAsync_UpdatesMostRecentUnresolvedItem()
    {
        const string key = "journal.no-entry-today";
        await _store.RecordEmittedAsync(new[] { Make(key) });

        await _store.RecordOutcomeAsync(key, InsightOutcome.ActedOn);

        var recent = await _store.GetRecentAsync(TimeSpan.FromHours(1));
        var updated = Assert.Single(recent);

        Assert.Equal(InsightOutcome.ActedOn, updated.Outcome);
        Assert.NotNull(updated.ResolvedAtUtc);
    }

    [Fact]
    public async Task RecordOutcomeAsync_NoOps_WhenNoItemExists()
    {
        // Must not throw even when no matching item is in the store.
        await _store.RecordOutcomeAsync("nonexistent.key", InsightOutcome.Dismissed);
    }

    [Fact]
    public async Task RecordOutcomeAsync_TargetsMostRecent_UnresolvedItem()
    {
        const string key = "journal.no-entry-today";

        // Record two emissions with a small gap so order is deterministic.
        await _store.RecordEmittedAsync(new[] { Make(key) });
        await Task.Delay(10);
        await _store.RecordEmittedAsync(new[] { Make(key) });

        await _store.RecordOutcomeAsync(key, InsightOutcome.ActedOn);

        var recent = await _store.GetRecentAsync(TimeSpan.FromHours(1));

        Assert.Equal(2, recent.Count);

        // Most recent (newest EmittedAtUtc) should be resolved.
        var newest = recent.OrderByDescending(item => item.EmittedAtUtc).First();
        Assert.Equal(InsightOutcome.ActedOn, newest.Outcome);

        // Older one remains Unknown.
        var older = recent.OrderBy(item => item.EmittedAtUtc).First();
        Assert.Equal(InsightOutcome.Unknown, older.Outcome);
    }
}
