using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CognitivePlatform.Api.Domains.Knowledge.Models;
using CognitivePlatform.Api.Integrations.Embeddings;

namespace CognitivePlatform.Api.Domains.Knowledge;

public interface IKnowledgeIngestionService
{
    Task<KnowledgeDomain> CreateDomainAsync(string name, string description, KnowledgeDomainMode mode);
    Task<KnowledgeDomain?> GetDomainAsync(string name);
    Task<IReadOnlyList<KnowledgeDomain>> ListDomainsAsync();
    Task<bool> DeleteDomainAsync(string name);

    Task<DomainKnowledgeObject> IngestDocumentAsync(string domainName, string title, string content, string source, List<string>? tags = null, CancellationToken ct = default);
    Task<DomainKnowledgeObject?> GetObjectAsync(string domainName, Guid objectId);
    Task<IReadOnlyList<DomainKnowledgeObject>> ListObjectsAsync(string domainName);
    Task<bool> DeleteObjectAsync(string domainName, Guid objectId, CancellationToken ct = default);
    Task<IReadOnlyList<VectorSearchResult>> RetrieveContextAsync(string domainName, string query, int limit = 3, CancellationToken ct = default);
}
