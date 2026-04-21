using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.KnowledgeInbox;
using CP.Shared.Primitives.Avails.Extensions;
using Microsoft.AspNetCore.Mvc;

namespace CognitivePlatform.Api.Controllers.Admin;

[Route("api/admin/knowledge")]
public sealed class AdminKnowledgeController : AdminControllerBase
{
    private readonly SqliteObjectStore _store;

    public AdminKnowledgeController( IConfiguration    configuration
                                   , SqliteObjectStore  store)
        : base(configuration)
    {
        _store = store;
    }

    /// <summary>
    /// Returns all knowledge items including soft-deleted, newest-modified first.
    /// Tags are projected to string[] to avoid IEnumerable serialisation issues.
    /// </summary>
    [HttpGet]
    public IActionResult GetAll()
    {
        if (IsAdminAuthorized().Not()) return Unauthorized401();

        var items = _store.ListIncludingDeleted<KnowledgeItemDto>()
                          .Select(item => new
                                  {
                                      IdString       = item.IdString
                                    , Kind           = item.Kind.ToString()
                                    , item.Title
                                    , item.Summary
                                    , Status         = item.Status.ToString()
                                    , item.CreatedAt
                                    , item.LastModifiedAt
                                  })
                          .OrderByDescending(item => item.LastModifiedAt)
                          .ToList();

        return Ok(items);
    }

    /// <summary>Permanently removes a knowledge item. Irreversible.</summary>
    [HttpDelete("{id}")]
    public IActionResult HardDelete(string id)
    {
        if (IsAdminAuthorized().Not()) return Unauthorized401();

        var deleted = _store.HardDelete<KnowledgeItemDto>(id);
        
        return deleted ? Ok() : NotFound();
    }

    /// <summary>Clears the soft-delete marker, restoring the item to active visibility.</summary>
    [HttpPost("{id}/restore")]
    public IActionResult Restore(string id)
    {
        if (IsAdminAuthorized().Not()) return Unauthorized401();

        var restored = _store.Undelete<KnowledgeItemDto>(id);
        
        return restored ? Ok() : NotFound();
    }

    /// <summary>Directly injects a new knowledge item — developer escape hatch.</summary>
    [HttpPost]
    public async Task<IActionResult> Inject([FromBody] InjectKnowledgeRequest request)
    {
        if (IsAdminAuthorized().Not()) return Unauthorized401();

        if (request.Title.HasNoValue())
            return BadRequest("Title is required.");

        if (Enum.TryParse<KnowledgeKind>(request.Kind, ignoreCase: true, out var kind).Not())
            kind = KnowledgeKind.Pending;

        var item = new KnowledgeItemDto
                   {
                       Id             = Guid.NewGuid()
                     , Kind           = kind
                     , Title          = request.Title.Trim()
                     , Summary        = request.Summary?.Trim()
                     , Status         = KnowledgeStatus.Active
                     , CreatedAt      = DateTimeOffset.UtcNow
                     , LastModifiedAt = DateTimeOffset.UtcNow
                   };

        var savedId = await _store.Save(item, id: item.IdString);
        
        return Ok(new { id = savedId });
    }
}

public sealed record InjectKnowledgeRequest
{
    public string  Title   { get; init; } = string.Empty;
    public string? Summary { get; init; }
    public string  Kind    { get; init; } = "Pending";
}
