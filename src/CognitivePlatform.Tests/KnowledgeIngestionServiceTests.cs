using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Domains.Document;
using CognitivePlatform.Api.Domains.Knowledge;
using CognitivePlatform.Api.Domains.Knowledge.Models;
using CognitivePlatform.Api.Integrations.Embeddings;
using Moq;
using Xunit;

namespace CognitivePlatform.Tests;

public class KnowledgeIngestionServiceTests
{
    private readonly Mock<IObjectStore> _objectStoreMock = new();
    private readonly Mock<IVectorStore> _vectorStoreMock = new();
    private readonly Mock<IEmbeddingService> _embeddingServiceMock = new();
    private readonly DocumentChunkingService _chunkingService = new();

    private readonly Dictionary<(string, string), object> _objectDb = new();
    private readonly List<VectorEntry> _vectorDb = new();

    private readonly KnowledgeIngestionService _service;

    public KnowledgeIngestionServiceTests()
    {
        // Set up in-memory IObjectStore behavior
        _objectStoreMock.Setup(store => store.Save(It.IsAny<object>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns<object, string, string>((val, partition, id) =>
            {
                var key = id ?? Guid.NewGuid().ToString();
                _objectDb[(partition ?? "", key)] = val;
                return Task.FromResult(key);
            });

        _objectStoreMock.Setup(store => store.GetAsync<KnowledgeDomain>(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<string, string, CancellationToken>((id, partition, ct) =>
            {
                if (_objectDb.TryGetValue((partition ?? "", id ?? ""), out var val))
                    return Task.FromResult((KnowledgeDomain?)val);
                return Task.FromResult<KnowledgeDomain?>(null);
            });

        _objectStoreMock.Setup(store => store.GetAsync<DomainKnowledgeObject>(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<string, string, CancellationToken>((id, partition, ct) =>
            {
                if (_objectDb.TryGetValue((partition ?? "", id ?? ""), out var val))
                    return Task.FromResult((DomainKnowledgeObject?)val);
                return Task.FromResult<DomainKnowledgeObject?>(null);
            });

        _objectStoreMock.Setup(store => store.Get<DomainKnowledgeObject>(It.IsAny<string>(), It.IsAny<string>()))
            .Returns<string, string>((id, partition) =>
            {
                if (_objectDb.TryGetValue((partition ?? "", id ?? ""), out var val))
                    return (DomainKnowledgeObject?)val;
                return null;
            });

        _objectStoreMock.Setup(store => store.ListAsync<KnowledgeDomain>(It.IsAny<string>(), It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .Returns<string, DateTimeOffset?, DateTimeOffset?, CancellationToken>((partition, from, to, ct) =>
            {
                var list = _objectDb.Where(kvp => kvp.Key.Item1 == partition)
                    .Select(kvp => kvp.Value)
                    .OfType<KnowledgeDomain>()
                    .ToList();
                return Task.FromResult<IReadOnlyList<KnowledgeDomain>>(list);
            });

        _objectStoreMock.Setup(store => store.ListAsync<DomainKnowledgeObject>(It.IsAny<string>(), It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .Returns<string, DateTimeOffset?, DateTimeOffset?, CancellationToken>((partition, from, to, ct) =>
            {
                var list = _objectDb.Where(kvp => kvp.Key.Item1 == partition)
                    .Select(kvp => kvp.Value)
                    .OfType<DomainKnowledgeObject>()
                    .ToList();
                return Task.FromResult<IReadOnlyList<DomainKnowledgeObject>>(list);
            });

        _objectStoreMock.Setup(store => store.SoftDelete<KnowledgeDomain>(It.IsAny<string>(), It.IsAny<string>()))
            .Returns<string, string>((id, partition) =>
            {
                return _objectDb.Remove((partition ?? "", id ?? ""));
            });

        _objectStoreMock.Setup(store => store.SoftDelete<DomainKnowledgeObject>(It.IsAny<string>(), It.IsAny<string>()))
            .Returns<string, string>((id, partition) =>
            {
                return _objectDb.Remove((partition ?? "", id ?? ""));
            });

        // Set up in-memory IVectorStore behavior
        _vectorStoreMock.Setup(v => v.SaveAsync(It.IsAny<VectorEntry>(), It.IsAny<CancellationToken>()))
            .Returns<VectorEntry, CancellationToken>((entry, ct) =>
            {
                _vectorDb.Add(entry);
                return Task.CompletedTask;
            });

        _vectorStoreMock.Setup(v => v.DeleteByReferenceAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<string, string, CancellationToken>((domain, refId, ct) =>
            {
                var removed = _vectorDb.RemoveAll(ve => ve.Domain == domain && ve.ReferenceId == refId);
                return Task.CompletedTask;
            });

        _vectorStoreMock.Setup(v => v.SearchAsync(It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<float[], int, string, CancellationToken>((emb, limit, domain, ct) =>
            {
                var matches = _vectorDb.Where(ve => ve.Domain == domain)
                    .Select(ve => new VectorSearchResult(ve, 0.95f))
                    .Take(limit)
                    .ToList();
                return Task.FromResult<IReadOnlyList<VectorSearchResult>>(matches);
            });

        // Embedding service mock setup
        _embeddingServiceMock.SetupGet(es => es.IsAvailable).Returns(true);
        _embeddingServiceMock.Setup(es => es.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[] { 0.1f, 0.2f, 0.3f });

        _service = new KnowledgeIngestionService(
            _objectStoreMock.Object,
            _vectorStoreMock.Object,
            _embeddingServiceMock.Object,
            _chunkingService);
    }

    [Fact]
    public async Task CreateDomainAsync_SavesDomainSuccessfully()
    {
        var domain = await _service.CreateDomainAsync("HR", "HR Policies", KnowledgeDomainMode.Strict);

        Assert.NotNull(domain);
        Assert.Equal("HR", domain.Name);
        Assert.Equal("HR Policies", domain.Description);
        Assert.Equal(KnowledgeDomainMode.Strict, domain.Mode);

        var retrieved = await _service.GetDomainAsync("hr");
        Assert.NotNull(retrieved);
        Assert.Equal("HR", retrieved.Name);
    }

    [Fact]
    public async Task ListDomainsAsync_ReturnsSortedDomains()
    {
        await _service.CreateDomainAsync("Finance", "Finance policies", KnowledgeDomainMode.Grounded);
        await _service.CreateDomainAsync("HR", "HR policies", KnowledgeDomainMode.Strict);

        var list = await _service.ListDomainsAsync();

        Assert.Equal(2, list.Count);
        Assert.Equal("Finance", list[0].Name);
        Assert.Equal("HR", list[1].Name);
    }

    [Fact]
    public async Task IngestDocumentAsync_SplitsEmbedsAndStoresChunks()
    {
        await _service.CreateDomainAsync("Legal", "Legal documents", KnowledgeDomainMode.Advisory);

        var doc = await _service.IngestDocumentAsync(
            "Legal",
            "Privacy Policy",
            "This is a long privacy policy document content that will be chunked.",
            "C:\\privacy.md",
            new List<string> { "privacy", "gdpr" });

        Assert.NotNull(doc);
        Assert.Equal("Legal", doc.DomainName);
        Assert.Equal("Privacy Policy", doc.Title);
        Assert.Contains("gdpr", doc.Tags);

        // Verify vectors were saved
        Assert.NotEmpty(_vectorDb);
        Assert.All(_vectorDb, entry =>
        {
            Assert.Equal("knowledge:legal", entry.Domain);
            Assert.Equal(doc.IdString, entry.ReferenceId);
            Assert.Equal(3, entry.Embedding.Length);
        });

        // Test retrieval
        var context = await _service.RetrieveContextAsync("Legal", "privacy policy query");
        Assert.NotEmpty(context);
        Assert.Equal(doc.IdString, context[0].Entry.ReferenceId);
    }

    [Fact]
    public async Task DeleteObjectAsync_RemovesObjectAndVectors()
    {
        await _service.CreateDomainAsync("Legal", "Legal documents", KnowledgeDomainMode.Advisory);

        var doc = await _service.IngestDocumentAsync(
            "Legal",
            "Terms of Service",
            "These are the terms of service...",
            "C:\\tos.txt");

        Assert.NotEmpty(_vectorDb);

        var deleted = await _service.DeleteObjectAsync("Legal", doc.Id);
        Assert.True(deleted);

        var objects = await _service.ListObjectsAsync("Legal");
        Assert.Empty(objects);
        Assert.Empty(_vectorDb);
    }
}
