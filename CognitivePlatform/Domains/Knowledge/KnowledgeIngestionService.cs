using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Domains.Document;
using CognitivePlatform.Api.Domains.Knowledge.Models;
using CognitivePlatform.Api.Integrations.Embeddings;

namespace CognitivePlatform.Api.Domains.Knowledge;

public sealed class KnowledgeIngestionService : IKnowledgeIngestionService
{
    private const string DomainPartition = "knowledge_domains";

    private readonly IObjectStore _objectStore;
    private readonly IVectorStore _vectorStore;
    private readonly IEmbeddingService _embeddingService;
    private readonly DocumentChunkingService _chunkingService;

    public KnowledgeIngestionService(
        IObjectStore objectStore,
        IVectorStore vectorStore,
        IEmbeddingService embeddingService,
        DocumentChunkingService chunkingService)
    {
        _objectStore = objectStore ?? throw new ArgumentNullException(nameof(objectStore));
        _vectorStore = vectorStore ?? throw new ArgumentNullException(nameof(vectorStore));
        _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
        _chunkingService = chunkingService ?? throw new ArgumentNullException(nameof(chunkingService));
    }

    public async Task<KnowledgeDomain> CreateDomainAsync(string name, string description, KnowledgeDomainMode mode)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Domain name cannot be empty.", nameof(name));

        var domain = new KnowledgeDomain
        {
            Name = name.Trim(),
            Description = description?.Trim() ?? string.Empty,
            Mode = mode,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var key = NormalizeDomainName(domain.Name);
        await _objectStore.Save(domain, DomainPartition, key);
        return domain;
    }

    public async Task<KnowledgeDomain?> GetDomainAsync(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var key = NormalizeDomainName(name);
        return await _objectStore.GetAsync<KnowledgeDomain>(key, DomainPartition);
    }

    public async Task<IReadOnlyList<KnowledgeDomain>> ListDomainsAsync()
    {
        var domains = await _objectStore.ListAsync<KnowledgeDomain>(DomainPartition);
        return domains.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<bool> DeleteDomainAsync(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        var domain = await GetDomainAsync(name);
        if (domain is null)
            return false;

        var key = NormalizeDomainName(name);

        // Delete all objects and their vectors
        var objects = await ListObjectsAsync(name);
        foreach (var obj in objects)
        {
            await DeleteObjectAsync(name, obj.Id);
        }

        // Soft delete the domain metadata
        return _objectStore.SoftDelete<KnowledgeDomain>(key, DomainPartition);
    }

    public async Task<DomainKnowledgeObject> IngestDocumentAsync(
        string domainName,
        string title,
        string content,
        string source,
        List<string>? tags = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(domainName))
            throw new ArgumentException("Domain name cannot be empty.", nameof(domainName));
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Title cannot be empty.", nameof(title));
        if (string.IsNullOrWhiteSpace(content))
            throw new ArgumentException("Content cannot be empty.", nameof(content));

        var domain = await GetDomainAsync(domainName);
        if (domain is null)
            throw new InvalidOperationException($"Knowledge domain '{domainName}' does not exist. Create it first.");

        var obj = new DomainKnowledgeObject
        {
            Id = Guid.NewGuid(),
            DomainName = domain.Name,
            Title = title.Trim(),
            Content = content,
            Source = source?.Trim() ?? string.Empty,
            IngestedAt = DateTimeOffset.UtcNow,
            Tags = tags ?? new List<string>()
        };

        var partition = GetObjectsPartition(domain.Name);
        await _objectStore.Save(obj, partition, obj.IdString);

        // Embed and index chunks semantically if embedding service is available
        if (_embeddingService.IsAvailable)
        {
            var chunks = _chunkingService.Chunk(content);
            var vectorDomain = GetVectorDomain(domain.Name);

            for (int i = 0; i < chunks.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var chunk = chunks[i];
                var embedding = await _embeddingService.EmbedAsync(chunk, ct);

                var entry = new VectorEntry
                {
                    Id = $"{vectorDomain}:{obj.IdString}:{i}",
                    Domain = vectorDomain,
                    ReferenceId = obj.IdString,
                    Text = chunk,
                    Embedding = embedding,
                    EmbeddedAt = DateTimeOffset.UtcNow
                };

                await _vectorStore.SaveAsync(entry, ct);
            }
        }

        return obj;
    }

    public async Task<DomainKnowledgeObject?> GetObjectAsync(string domainName, Guid objectId)
    {
        if (string.IsNullOrWhiteSpace(domainName))
            return null;

        var partition = GetObjectsPartition(domainName);
        return await _objectStore.GetAsync<DomainKnowledgeObject>(objectId.ToString(), partition);
    }

    public async Task<IReadOnlyList<DomainKnowledgeObject>> ListObjectsAsync(string domainName)
    {
        if (string.IsNullOrWhiteSpace(domainName))
            return Array.Empty<DomainKnowledgeObject>();

        var partition = GetObjectsPartition(domainName);
        var objects = await _objectStore.ListAsync<DomainKnowledgeObject>(partition);
        return objects.OrderByDescending(o => o.IngestedAt).ToList();
    }

    public async Task<bool> DeleteObjectAsync(string domainName, Guid objectId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(domainName))
            return false;

        var partition = GetObjectsPartition(domainName);
        var idStr = objectId.ToString();

        var existing = _objectStore.Get<DomainKnowledgeObject>(idStr, partition);
        if (existing is null)
            return false;

        // Delete vectors from vector store
        var vectorDomain = GetVectorDomain(domainName);
        await _vectorStore.DeleteByReferenceAsync(vectorDomain, idStr, ct);

        // Soft delete object metadata
        return _objectStore.SoftDelete<DomainKnowledgeObject>(idStr, partition);
    }

    public async Task<IReadOnlyList<VectorSearchResult>> RetrieveContextAsync(
        string domainName,
        string query,
        int limit = 3,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(domainName) || string.IsNullOrWhiteSpace(query))
            return Array.Empty<VectorSearchResult>();

        if (!_embeddingService.IsAvailable)
            return Array.Empty<VectorSearchResult>();

        var embedding = await _embeddingService.EmbedAsync(query, ct);
        var vectorDomain = GetVectorDomain(domainName);

        return await _vectorStore.SearchAsync(embedding, topK: limit, domain: vectorDomain, ct: ct);
    }

    // Helpers
    private static string NormalizeDomainName(string name) => name.Trim().ToLowerInvariant();
    private static string GetObjectsPartition(string domainName) => $"knowledge_objects_{NormalizeDomainName(domainName)}";
    private static string GetVectorDomain(string domainName) => $"knowledge:{NormalizeDomainName(domainName)}";
}
