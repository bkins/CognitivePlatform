using CognitivePlatform.Api.Domains.Tasks;
using CP.Shared.Primitives.Avails.Extensions;
using Microsoft.AspNetCore.Mvc;

namespace CognitivePlatform.Api.Controllers;

[ApiController]
[Route("api/tasks")]
public sealed class TaskController : ControllerBase
{
    private readonly ITaskService       _service;
    private readonly IDailyBriefService _brief;

    public TaskController( ITaskService       service
                         , IDailyBriefService brief )
    {
        _service = service;
        _brief   = brief;
    }

    [HttpGet("{id:guid}")]
    public ActionResult<TaskDto> GetById( [FromRoute] Guid id )
    {
        var task = _service.Get(id);

        if (task is null || task.IsDeleted)
            return NotFound();

        return Ok(TaskDto.From(task));
    }

    [HttpGet]
    public ActionResult<IReadOnlyList<TaskDto>> GetActive()
    {
        var tasks = _service.GetActive();

        return Ok(tasks.Select(TaskDto.From)
                       .ToList());
    }

    [HttpDelete("{id:guid}")]
    public IActionResult Delete( [FromRoute] Guid id )
    {
        var task = _service.Get(id);
        
        if (task is null || task.IsDeleted)
            return NotFound();

        _service.Delete(id);

        return Ok($"Task '{task.ShortDescription}' deleted ({task.Id}).");
    }

    [HttpGet("brief")]
    public ActionResult<string> GetBrief( [FromQuery] string? date = null )
    {
        if (date.HasNoValue())
            return Ok(_brief.GetBrief());

        if (DateOnly.TryParse(date, out var localDate).Not())
            return BadRequest("Date must be in yyyy-MM-dd format (e.g. 2026-04-24).");

        return Ok(_brief.GetBrief(localDate));
    }

    [HttpPut("{id:guid}")]
    public ActionResult<TaskDto> Undelete( [FromRoute] Guid id )
    {
        var task = _service.GetDeleted(id);
        if (task is null || task.IsDeleted.Not())
            return NotFound();
        
        _service.UnDelete(id);
        
        return Ok($"Task '{task.ShortDescription}' Undeleted ({task.Id}).");
    }
}