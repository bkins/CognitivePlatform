using CognitivePlatform.Api.Domains.Journal;
using CognitivePlatform.Api.Domains.Tasks;
using Microsoft.AspNetCore.Mvc;

namespace CognitivePlatform.Api.Controllers;

[ApiController]
[Route("api/tasks")]
public sealed class TaskController : ControllerBase
{
    private readonly ITaskService _service;

    public TaskController(ITaskService service)
    {
        _service = service;
    }

    [HttpGet("{id:guid}")]
    public ActionResult<TaskDto> GetById(Guid id, CancellationToken ct)
    {
        var item = _service.GetById(id);

        if (item is null) return NotFound();
        
        IReadOnlyList<string> tags = item.Tags is { Count: > 0 }
                                             ? item.Tags
                                             : Array.Empty<string>();
        return Ok(new TaskDto
                  {
                          Id        = id
                        , Text      = item.Title
                        , CreatedAt = item.CreatedAt
                        , Tags      = tags
                  });
    }
}
