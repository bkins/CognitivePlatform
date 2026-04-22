using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.KnowledgeInbox.Interfaces;

namespace CognitivePlatform.Api.KnowledgeInbox;

/// <summary>
/// ObjectStore is infrastructure.
/// Domain Services own meaning.
/// KnowledgeService coordinates meaning across domains.
///
/// [domain]Entry       ← immutable-ish content
/// KnowledgeItem       ← system-facing representation
/// KnowledgeStatus     ← lifecycle state
/// </summary>
public sealed class KnowledgeService : IKnowledgeService
{
    private readonly IEnumerable<IKnowledgeSource> _sources;
    private readonly IObjectStore                  _objectStore;
    
    public KnowledgeService (IEnumerable<IKnowledgeSource> sources
                          ,  IObjectStore                  objectStore)
    {
        _sources     = sources;
        _objectStore = objectStore;
    }

    // NOTE: KnowledgeService is a read-model aggregator.
    // Filtering, sorting, and pagination live here by design.
    public IReadOnlyList<KnowledgeItemDto> GetKnowledge (KnowledgeQuery    query
                                                       , CancellationToken ct)
    {
        var items = _sources.Where(source => query.Kind  == null
                                          || source.Kind == query.Kind)
                            .SelectMany(source => source.GetKnowledgeItems(query, ct))
                            .OrderByDescending(itemDto => itemDto.LastModifiedAt)
                            .ToList();

        return items;
    }

    public IReadOnlyList<ObjectHeader> ListHeaders (string          type
                                                  , DateTimeOffset? fromUtc = null
                                                  , DateTimeOffset? toUtc   = null)
    {
        return _sources
               .Where(source => string.IsNullOrEmpty(type)
                             || source.Kind.ToString().Equals(type, StringComparison.OrdinalIgnoreCase))
               .SelectMany(source => source.ListHeaders(fromUtc, toUtc))
               .OrderByDescending(header => header.CreatedUtc)
               .ThenBy(header => header.Id)
               .ToList();
    }

    public void Archive(Guid id, CancellationToken ct)
    {
        // Find the source that owns this knowledge item
        var source = _sources.FirstOrDefault(source => source.GetKnowledgeItems(new KnowledgeQuery { Id = id }
                                                                              , ct)
                                                             .Any());
        if (source is null) return;

        source.Archive(id, ct);
    }

    //
    // public void Delete(Guid id, CancellationToken ct)
    // {
    //     var source = ResolveSource(id);
    //     source.Delete(id, ct);
    // }

}

public record ObjectHeader
(
    string         Id
  , string         Type
  , DateTimeOffset CreatedUtc
  , DateTimeOffset UpdatedUtc
);
