using CognitivePlatform.Api.Integrations.Embeddings;

namespace CognitivePlatform.Tests;

public sealed class SqliteVectorStoreTests : IDisposable
{
    private readonly string            _dbPath;
    private readonly SqliteVectorStore _store;

    public SqliteVectorStoreTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"vectors_test_{Guid.NewGuid():N}.db");
        _store  = new SqliteVectorStore($"Data Source={_dbPath}");
    }

    public void Dispose()
    {
        try { File.Delete(_dbPath); }
        catch { /* ignore cleanup failures on locked files */ }
    }

    // ================================================================
    // SaveAsync / SearchAsync — round-trip
    // ================================================================

    [Fact]
    public async Task SaveAsync_And_SearchAsync_RoundTrip()
    {
        var entry = new VectorEntry
                    {
                        Id          = "e1"
                      , Domain      = "journal"
                      , ReferenceId = "ref-1"
                      , Text        = "had a great day"
                      , Embedding   = new float[] { 1f, 0f, 0f }
                    };

        await _store.SaveAsync(entry);

        var results = await _store.SearchAsync(new float[] { 1f, 0f, 0f });

        Assert.Single(results);
        Assert.Equal("e1",              results[0].Entry.Id);
        Assert.Equal("journal",         results[0].Entry.Domain);
        Assert.Equal("ref-1",           results[0].Entry.ReferenceId);
        Assert.Equal("had a great day", results[0].Entry.Text);
    }

    [Fact]
    public async Task SearchAsync_ReturnsEmpty_WhenStoreIsEmpty()
    {
        var results = await _store.SearchAsync(new float[] { 1f, 0f, 0f });

        Assert.Empty(results);
    }

    // ================================================================
    // Cosine similarity ordering
    // ================================================================

    [Fact]
    public async Task SearchAsync_OrdersByCosineSimilarity_Descending()
    {
        var close  = new VectorEntry { Id = "close",  Domain = "journal", ReferenceId = "r1", Text = "a", Embedding = new float[] { 1f, 0.1f, 0f  } };
        var far    = new VectorEntry { Id = "far",    Domain = "journal", ReferenceId = "r2", Text = "b", Embedding = new float[] { 0f, 0f,   1f  } };
        var medium = new VectorEntry { Id = "medium", Domain = "journal", ReferenceId = "r3", Text = "c", Embedding = new float[] { 0.5f, 0.5f, 0f } };

        await _store.SaveAsync(close);
        await _store.SaveAsync(far);
        await _store.SaveAsync(medium);

        var results = await _store.SearchAsync(new float[] { 1f, 0f, 0f }, topK: 3);

        Assert.Equal(3, results.Count);
        Assert.Equal("close", results[0].Entry.Id);
        Assert.True(results[0].Score > results[1].Score);
        Assert.True(results[1].Score > results[2].Score);
    }

    // ================================================================
    // Domain filter
    // ================================================================

    [Fact]
    public async Task SearchAsync_FiltersByDomain()
    {
        var journalEntry = new VectorEntry { Id = "j1", Domain = "journal", ReferenceId = "r1", Text = "journal text", Embedding = new float[] { 1f, 0f } };
        var taskEntry    = new VectorEntry { Id = "t1", Domain = "tasks",   ReferenceId = "r1", Text = "task text",    Embedding = new float[] { 1f, 0f } };

        await _store.SaveAsync(journalEntry);
        await _store.SaveAsync(taskEntry);

        var results = await _store.SearchAsync(new float[] { 1f, 0f }, domain: "journal");

        Assert.Single(results);
        Assert.Equal("j1", results[0].Entry.Id);
    }

    [Fact]
    public async Task SearchAsync_ReturnsAllDomains_WhenDomainIsNull()
    {
        var journalEntry = new VectorEntry { Id = "j1", Domain = "journal", ReferenceId = "r1", Text = "j", Embedding = new float[] { 1f, 0f } };
        var taskEntry    = new VectorEntry { Id = "t1", Domain = "tasks",   ReferenceId = "r2", Text = "t", Embedding = new float[] { 1f, 0f } };

        await _store.SaveAsync(journalEntry);
        await _store.SaveAsync(taskEntry);

        var results = await _store.SearchAsync(new float[] { 1f, 0f }, topK: 10);

        Assert.Equal(2, results.Count);
    }

    // ================================================================
    // topK limit
    // ================================================================

    [Fact]
    public async Task SearchAsync_RespectsTopK()
    {
        for (int i = 0; i < 10; i++)
            await _store.SaveAsync(new VectorEntry
                                   {
                                       Id          = $"e{i}"
                                     , Domain      = "d"
                                     , ReferenceId = $"r{i}"
                                     , Text        = "t"
                                     , Embedding   = new float[] { (float)i / 10f, 0f }
                                   });

        var results = await _store.SearchAsync(new float[] { 1f, 0f }, topK: 3);

        Assert.Equal(3, results.Count);
    }

    // ================================================================
    // DeleteAsync
    // ================================================================

    [Fact]
    public async Task DeleteAsync_RemovesEntry()
    {
        var entry = new VectorEntry { Id = "del-1", Domain = "journal", ReferenceId = "r1", Text = "to delete", Embedding = new float[] { 1f } };

        await _store.SaveAsync(entry);
        await _store.DeleteAsync("del-1");

        var results = await _store.SearchAsync(new float[] { 1f });

        Assert.Empty(results);
    }

    [Fact]
    public async Task DeleteAsync_IsIdempotent_WhenEntryDoesNotExist()
    {
        await _store.DeleteAsync("nonexistent-id");

        var results = await _store.SearchAsync(new float[] { 1f });

        Assert.Empty(results);
    }

    // ================================================================
    // DeleteByReferenceAsync
    // ================================================================

    [Fact]
    public async Task DeleteByReferenceAsync_RemovesAllEntriesForReference()
    {
        var entry1 = new VectorEntry { Id = "ref-a-1", Domain = "journal", ReferenceId = "ref-a", Text = "first",  Embedding = new float[] { 1f, 0f } };
        var entry2 = new VectorEntry { Id = "ref-a-2", Domain = "journal", ReferenceId = "ref-a", Text = "second", Embedding = new float[] { 0f, 1f } };
        var keep   = new VectorEntry { Id = "ref-b-1", Domain = "journal", ReferenceId = "ref-b", Text = "third",  Embedding = new float[] { 0.5f, 0.5f } };

        await _store.SaveAsync(entry1);
        await _store.SaveAsync(entry2);
        await _store.SaveAsync(keep);

        await _store.DeleteByReferenceAsync("journal", "ref-a");

        var results = await _store.SearchAsync(new float[] { 1f, 0f }, topK: 10);

        Assert.Single(results);
        Assert.Equal("ref-b-1", results[0].Entry.Id);
    }

    [Fact]
    public async Task DeleteByReferenceAsync_DoesNotDeleteAcrossDomains()
    {
        var journalEntry = new VectorEntry { Id = "j-ref", Domain = "journal", ReferenceId = "shared-ref", Text = "j", Embedding = new float[] { 1f } };
        var taskEntry    = new VectorEntry { Id = "t-ref", Domain = "tasks",   ReferenceId = "shared-ref", Text = "t", Embedding = new float[] { 1f } };

        await _store.SaveAsync(journalEntry);
        await _store.SaveAsync(taskEntry);

        await _store.DeleteByReferenceAsync("journal", "shared-ref");

        var results = await _store.SearchAsync(new float[] { 1f }, topK: 10);

        Assert.Single(results);
        Assert.Equal("t-ref", results[0].Entry.Id);
    }

    // ================================================================
    // EvictOlderThanAsync
    // ================================================================

    [Fact]
    public async Task EvictOlderThanAsync_DeletesExpiredEntries()
    {
        var oldEntry = new VectorEntry
                       {
                           Id          = "old-entry"
                         , Domain      = "journal"
                         , ReferenceId = "r1"
                         , Text        = "old text"
                         , Embedding   = new float[] { 1f, 0f }
                         , EmbeddedAt  = DateTimeOffset.UtcNow.AddDays(-10)
                       };

        var newEntry = new VectorEntry
                       {
                           Id          = "new-entry"
                         , Domain      = "journal"
                         , ReferenceId = "r2"
                         , Text        = "new text"
                         , Embedding   = new float[] { 0f, 1f }
                         , EmbeddedAt  = DateTimeOffset.UtcNow
                       };

        await _store.SaveAsync(oldEntry);
        await _store.SaveAsync(newEntry);

        var evicted = await _store.EvictOlderThanAsync(TimeSpan.FromDays(7));

        Assert.Equal(1, evicted);

        var results = await _store.SearchAsync(new float[] { 1f, 0f }, topK: 10);
        Assert.Single(results);
        Assert.Equal("new-entry", results[0].Entry.Id);
    }
}
