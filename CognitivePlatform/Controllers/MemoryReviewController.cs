using CognitivePlatform.Api.Domains.MemoryReview;
using Microsoft.AspNetCore.Mvc;

namespace CognitivePlatform.Api.Controllers;

[ApiController]
[Route("api/memory/review")]
public sealed class MemoryReviewController : ControllerBase
{
    private readonly IMemoryReviewService _service;

    public MemoryReviewController(IMemoryReviewService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<MemoryReviewResult>> Get(CancellationToken cancellationToken)
    {
        return Ok(await _service.GetReviewAsync(cancellationToken));
    }
}
