using Moq;
using CognitivePlatform.Api.Domains.Search;
using CognitivePlatform.Api.Domains.Journal.Interfaces;
using CognitivePlatform.Api.Domains.Journal;
using CognitivePlatform.Api.Domains.Tasks;
using CognitivePlatform.Api.Domains.BrainDump;
using CognitivePlatform.Api.Integrations.Embeddings;

namespace CognitivePlatform.Tests;

public class SemanticSearchActionsTests
{
    private readonly Mock<IEmbeddingService> _embeddingMock = new();
    private readonly Mock<IVectorStore>      _vectorStoreMock = new();
    private readonly SemanticSearchActions   _actions;

    public SemanticSearchActionsTests()
    {
        _actions = new SemanticSearchActions(_embeddingMock.Object, _vectorStoreMock.Object);
    }

    // ================================================================
    // SemanticSearch — unavailable
    // ================================================================

    [Fact]
    public async Task SemanticSearch_ReturnsUnavailableMessage_WhenEmbeddingServiceNotAvailable()
    {
        _embeddingMock.SetupGet(service => service.IsAvailable).Returns(false);

        var result = await _actions.SemanticSearch("burnout");

        Assert.Contains("nomic-embed-text", result);
        Assert.Contains("Ollama",           result);
    }

    // ================================================================
    // SemanticSearch — results present
    // ================================================================

    [Fact]
    public async Task SemanticSearch_ReturnsFormattedResults_WhenResultsExist()
    {
        var embedding = new float[] { 1f, 0f };
        var entry     = new VectorEntry
                        {
                            Id          = "journal:abc"
                          , Domain      = "journal"
                          , ReferenceId = "abc"
                          , Text        = "Feeling burned out from work lately."
                          , Embedding   = embedding
                        };

        _embeddingMock.SetupGet(service => service.IsAvailable).Returns(true);
        _embeddingMock.Setup(service => service.EmbedAsync("burnout", default))
                      .ReturnsAsync(embedding);
        _vectorStoreMock.Setup(store => store.SearchAsync(embedding, 5, null, default))
                        .ReturnsAsync(new List<VectorSearchResult> { new(entry, 0.92f) });

        var result = await _actions.SemanticSearch("burnout");

        Assert.Contains("[journal]",                           result);
        Assert.Contains("abc",                                 result);
        Assert.Contains("0.92",                               result);
        Assert.Contains("Feeling burned out from work lately", result);
    }

    // ================================================================
    // SemanticSearch — no results
    // ================================================================

    [Fact]
    public async Task SemanticSearch_ReturnsNoResultsMessage_WhenStoreIsEmpty()
    {
        var embedding = new float[] { 1f, 0f };

        _embeddingMock.SetupGet(service => service.IsAvailable).Returns(true);
        _embeddingMock.Setup(service => service.EmbedAsync("dragons", default))
                      .ReturnsAsync(embedding);
        _vectorStoreMock.Setup(store => store.SearchAsync(embedding, 5, null, default))
                        .ReturnsAsync(new List<VectorSearchResult>());

        var result = await _actions.SemanticSearch("dragons");

        Assert.Contains("No results found",  result);
        Assert.Contains("'dragons'",          result);
    }

    // ================================================================
    // SemanticSearch — results sorted by score (caller responsibility, but format check)
    // ================================================================

    [Fact]
    public async Task SemanticSearch_RendersResultsInOrderProvided()
    {
        var embedding = new float[] { 1f, 0f };
        var high      = new VectorEntry { Id = "j:1", Domain = "journal", ReferenceId = "1", Text = "first",  Embedding = embedding };
        var low       = new VectorEntry { Id = "j:2", Domain = "journal", ReferenceId = "2", Text = "second", Embedding = embedding };

        _embeddingMock.SetupGet(service => service.IsAvailable).Returns(true);
        _embeddingMock.Setup(service => service.EmbedAsync("test", default)).ReturnsAsync(embedding);
        _vectorStoreMock.Setup(store => store.SearchAsync(embedding, 5, null, default))
                        .ReturnsAsync(new List<VectorSearchResult>
                                      {
                                              new(high, 0.95f)
                                            , new(low,  0.60f)
                                      });

        var result = await _actions.SemanticSearch("test");

        var firstIdx  = result.IndexOf("0.95", StringComparison.Ordinal);
        var secondIdx = result.IndexOf("0.60", StringComparison.Ordinal);

        Assert.True(firstIdx < secondIdx);
    }

    // ================================================================
    // SemanticSearch — long text truncated to snippet
    // ================================================================

    [Fact]
    public async Task SemanticSearch_TruncatesLongText_ToSnippet()
    {
        var embedding = new float[] { 1f, 0f };
        var longText  = new string('x', 300);
        var entry     = new VectorEntry { Id = "j:1", Domain = "journal", ReferenceId = "1", Text = longText, Embedding = embedding };

        _embeddingMock.SetupGet(service => service.IsAvailable).Returns(true);
        _embeddingMock.Setup(service => service.EmbedAsync("anything", default)).ReturnsAsync(embedding);
        _vectorStoreMock.Setup(store => store.SearchAsync(embedding, 5, null, default))
                        .ReturnsAsync(new List<VectorSearchResult> { new(entry, 0.80f) });

        var result = await _actions.SemanticSearch("anything");

        Assert.Contains("…", result);
    }

    // ================================================================
    // SemanticSearchInDomain — filters by domain
    // ================================================================

    [Fact]
    public async Task SemanticSearchInDomain_PassesDomainFilter_ToVectorStore()
    {
        var embedding = new float[] { 1f, 0f };

        _embeddingMock.SetupGet(service => service.IsAvailable).Returns(true);
        _embeddingMock.Setup(service => service.EmbedAsync("anxiety", default)).ReturnsAsync(embedding);
        _vectorStoreMock.Setup(store => store.SearchAsync(embedding, 5, "journal", default))
                        .ReturnsAsync(new List<VectorSearchResult>());

        await _actions.SemanticSearchInDomain("journal", "anxiety");

        _vectorStoreMock.Verify(
            store => store.SearchAsync(embedding, 5, "journal", default)
          , Times.Once);
    }

    [Fact]
    public async Task SemanticSearchInDomain_ReturnsNoResultsMessage_WhenStoreIsEmpty()
    {
        var embedding = new float[] { 1f, 0f };

        _embeddingMock.SetupGet(service => service.IsAvailable).Returns(true);
        _embeddingMock.Setup(service => service.EmbedAsync("dragons", default)).ReturnsAsync(embedding);
        _vectorStoreMock.Setup(store => store.SearchAsync(embedding, 5, "task", default))
                        .ReturnsAsync(new List<VectorSearchResult>());

        var result = await _actions.SemanticSearchInDomain("task", "dragons");

        Assert.Contains("No task results found", result);
        Assert.Contains("'dragons'",              result);
    }

    // ================================================================
    // FindSimilar — source entry not found
    // ================================================================

    [Fact]
    public async Task FindSimilar_ReturnsNotFoundMessage_WhenSourceEntryMissing()
    {
        _embeddingMock.SetupGet(service => service.IsAvailable).Returns(true);
        _vectorStoreMock.Setup(store => store.GetByReferenceAsync("journal", "missing-id", default))
                        .ReturnsAsync((VectorEntry?)null);

        var result = await _actions.FindSimilar("journal", "missing-id");

        Assert.Contains("No indexed content found", result);
        Assert.Contains("missing-id",               result);
    }

    // ================================================================
    // FindSimilar — excludes the source entry from results
    // ================================================================

    [Fact]
    public async Task FindSimilar_ExcludesSourceEntry_FromResults()
    {
        var embedding = new float[] { 1f, 0f };
        var source    = new VectorEntry { Id = "j:src", Domain = "journal", ReferenceId = "src", Text = "source text", Embedding = embedding };
        var similar   = new VectorEntry { Id = "j:sim", Domain = "journal", ReferenceId = "sim", Text = "similar",     Embedding = embedding };

        _embeddingMock.SetupGet(service => service.IsAvailable).Returns(true);
        _vectorStoreMock.Setup(store => store.GetByReferenceAsync("journal", "src", default))
                        .ReturnsAsync(source);
        _vectorStoreMock.Setup(store => store.SearchAsync(embedding, 6, "journal", default))
                        .ReturnsAsync(new List<VectorSearchResult>
                                      {
                                              new(source,  1.00f)
                                            , new(similar, 0.85f)
                                      });

        var result = await _actions.FindSimilar("journal", "src");

        Assert.DoesNotContain("score: 1.00", result);
        Assert.Contains("sim",               result);
        Assert.Contains("0.85",              result);
    }

    // ================================================================
    // FindSimilar — unavailable
    // ================================================================

    [Fact]
    public async Task FindSimilar_ReturnsUnavailableMessage_WhenEmbeddingServiceNotAvailable()
    {
        _embeddingMock.SetupGet(service => service.IsAvailable).Returns(false);

        var result = await _actions.FindSimilar("journal", "some-id");

        Assert.Contains("nomic-embed-text", result);
    }

    [Fact]
    public async Task SemanticSearch_ResolvesPositionsInFormatResults()
    {
        var journalMock   = new Mock<IJournalService>();
        var taskMock      = new Mock<ITaskService>();
        var brainDumpMock = new Mock<IBrainDumpService>();

        var entryId = "journal-entry-guid";
        var journalEntry = new JournalEntry { Id = entryId };
        var revision = new JournalRevision();
        var entryWithRevision = new CognitivePlatform.Api.Models.JournalEntryWithRevision(journalEntry, revision, false);
        var entries = new List<(int Position, CognitivePlatform.Api.Models.JournalEntryWithRevision EntryWithRevision)>
                      {
                          (5, entryWithRevision)
                      };
        journalMock.Setup(j => j.GetOrderedEntries()).Returns(entries);

        var actionsWithServices = new SemanticSearchActions(
            _embeddingMock.Object
          , _vectorStoreMock.Object
          , journalMock.Object
          , taskMock.Object
          , brainDumpMock.Object);

        var embedding = new float[] { 1f, 0f };
        var entry     = new VectorEntry
                        {
                            Id          = "journal:" + entryId
                          , Domain      = "journal"
                          , ReferenceId = entryId
                          , Text        = "Looking back at the logs."
                          , Embedding   = embedding
                        };

        _embeddingMock.SetupGet(service => service.IsAvailable).Returns(true);
        _embeddingMock.Setup(service => service.EmbedAsync("burnout", default))
                      .ReturnsAsync(embedding);
        _vectorStoreMock.Setup(store => store.SearchAsync(embedding, 5, null, default))
                        .ReturnsAsync(new List<VectorSearchResult> { new(entry, 0.92f) });

        var result = await actionsWithServices.SemanticSearch("burnout");

        Assert.Contains("[journal] entry #5", result);
        Assert.DoesNotContain(entryId, result);
    }
}
