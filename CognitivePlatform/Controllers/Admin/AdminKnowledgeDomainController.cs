using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CognitivePlatform.Api.Domains.Knowledge;
using CognitivePlatform.Api.Domains.Knowledge.Models;
using CP.Shared.Primitives.Avails.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace CognitivePlatform.Api.Controllers.Admin;

[Route("api/admin/knowledge/domains")]
public sealed class AdminKnowledgeDomainController : AdminControllerBase
{
    private readonly IKnowledgeIngestionService _ingestionService;

    public AdminKnowledgeDomainController(
        IConfiguration configuration,
        IKnowledgeIngestionService ingestionService)
        : base(configuration)
    {
        _ingestionService = ingestionService ?? throw new ArgumentNullException(nameof(ingestionService));
    }

    [HttpPost]
    public async Task<IActionResult> CreateDomain([FromBody] CreateDomainRequest request)
    {
        if (IsAdminAuthorized().Not()) return Unauthorized401();

        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("Domain name is required.");

        if (!Enum.TryParse<KnowledgeDomainMode>(request.Mode, true, out var mode))
            mode = KnowledgeDomainMode.Grounded;

        var domain = await _ingestionService.CreateDomainAsync(request.Name, request.Description, mode);
        return Ok(domain);
    }

    [HttpGet]
    public async Task<IActionResult> ListDomains()
    {
        if (IsAdminAuthorized().Not()) return Unauthorized401();

        var list = await _ingestionService.ListDomainsAsync();
        return Ok(list);
    }

    [HttpDelete("{name}")]
    public async Task<IActionResult> DeleteDomain(string name)
    {
        if (IsAdminAuthorized().Not()) return Unauthorized401();

        var deleted = await _ingestionService.DeleteDomainAsync(name);
        return deleted ? Ok() : NotFound();
    }

    [HttpPost("{name}/ingest")]
    public async Task<IActionResult> IngestDocument(string name, [FromBody] IngestDocumentRequest request, CancellationToken ct)
    {
        if (IsAdminAuthorized().Not()) return Unauthorized401();

        if (string.IsNullOrWhiteSpace(request.Title))
            return BadRequest("Document title is required.");
        if (string.IsNullOrWhiteSpace(request.Content))
            return BadRequest("Document content is required.");

        try
        {
            var obj = await _ingestionService.IngestDocumentAsync(
                name,
                request.Title,
                request.Content,
                request.Source,
                request.Tags,
                ct);

            return Ok(obj);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet("{name}/objects")]
    public async Task<IActionResult> ListObjects(string name)
    {
        if (IsAdminAuthorized().Not()) return Unauthorized401();

        var list = await _ingestionService.ListObjectsAsync(name);
        return Ok(list);
    }

    [HttpDelete("{name}/objects/{id:guid}")]
    public async Task<IActionResult> DeleteObject(string name, Guid id, CancellationToken ct)
    {
        if (IsAdminAuthorized().Not()) return Unauthorized401();

        var deleted = await _ingestionService.DeleteObjectAsync(name, id, ct);
        return deleted ? Ok() : NotFound();
    }
}

public sealed record CreateDomainRequest
{
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Mode { get; init; } = "Grounded";
}

public sealed record IngestDocumentRequest
{
    public string Title { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public List<string> Tags { get; init; } = new();
}
