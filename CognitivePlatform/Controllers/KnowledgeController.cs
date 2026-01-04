using CognitivePlatform.Api.Avails;
using CognitivePlatform.Api.Domains.Journal;
using CognitivePlatform.Api.KnowledgeInbox;
using CognitivePlatform.Api.KnowledgeInbox.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace CognitivePlatform.Api.Controllers;

/// <summary>
/// Layer               -> Responsibility
/// Controller          -> Accept intent
/// KnowledgeService    -> Route intent
/// KnowledgeSource     -> Interpret intent 
/// ObjectStore	        -> Persist change
///
/// For example:
/// Client (API Caller -- MAUI, Website, CLI, etc)
///   ↓ 
/// KnowledgeController
///   ↓
/// KnowledgeService
///   ↓
/// JournalKnowledgeSource
///   ↓
/// SqliteObjectStore
///  
/// </summary>
[ApiController]
[Route("api/knowledge")]
public sealed class KnowledgeController : ControllerBase
{
    private readonly IKnowledgeService _knowledgeService;

    public KnowledgeController (IKnowledgeService knowledgeService)
    {
        _knowledgeService = knowledgeService;
    }

    [HttpGet]
    public ActionResult<IReadOnlyList<KnowledgeItemDto>> Get ([FromQuery] KnowledgeKind?   kind
                                                            , [FromQuery] KnowledgeStatus? status
                                                            , [FromQuery] string?          tag
                                                            , [FromQuery] string?          search
                                                            , CancellationToken            ct)
    {
        var query = new KnowledgeQuery
                    {
                            Kind   = kind
                          , Status = status
                          , Tag    = tag
                          , Search = search
                    };

        return Ok(_knowledgeService.GetKnowledge(query, ct));
    }
    
    /*
     * POST /api/knowledge/{id}/archive
     * POST /api/knowledge/{id}/delete
     */
    
    [HttpPost("{id:guid}/archive")]
    public IActionResult Archive(Guid id, CancellationToken ct)
    {
        _knowledgeService.Archive(id, ct);
        
        return NoContent();
    }
}


