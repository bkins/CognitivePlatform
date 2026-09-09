using CognitivePlatform.Api.Domains.Backlog;
using CP.Shared.Primitives.Avails.Extensions;
using Microsoft.AspNetCore.Mvc;

namespace CognitivePlatform.Api.Controllers.Admin;

[Route("api/admin/backlog")]
public sealed class AdminBacklogController : AdminControllerBase
{
    private readonly IBacklogBoardService _service;

    public AdminBacklogController( IConfiguration       configuration
                                 , IBacklogBoardService service)
        : base(configuration)
    {
        _service = service;
    }

    [HttpGet("board")]
    public async Task<IActionResult> GetBoard(CancellationToken cancellationToken)
    {
        if (IsAdminAuthorized().Not()) return Unauthorized401();
        return Ok(await _service.GetBoardAsync(cancellationToken));
    }

    [HttpPost("stories")]
    public async Task<IActionResult> CreateStory(CreateBacklogStoryRequest request, CancellationToken cancellationToken)
    {
        if (IsAdminAuthorized().Not()) return Unauthorized401();
        return await ExecuteAsync(() => _service.CreateStoryAsync(request with { Actor = "cp-admin" }, cancellationToken));
    }

    [HttpPatch("stories/{storyId:guid}")]
    public async Task<IActionResult> UpdateStory(Guid storyId, UpdateBacklogStoryRequest request, CancellationToken cancellationToken)
    {
        if (IsAdminAuthorized().Not()) return Unauthorized401();
        return await ExecuteAsync(() => _service.UpdateStoryAsync(storyId, request with { Actor = "cp-admin" }, cancellationToken));
    }

    [HttpPost("stories/{storyId:guid}/move")]
    public async Task<IActionResult> MoveStory(Guid storyId, MoveBacklogStoryRequest request, CancellationToken cancellationToken)
    {
        if (IsAdminAuthorized().Not()) return Unauthorized401();
        return await ExecuteAsync(() => _service.MoveStoryAsync(storyId, request with { Actor = "cp-admin" }, cancellationToken));
    }

    [HttpPost("stories/{storyId:guid}/archive")]
    public async Task<IActionResult> ArchiveStory(Guid storyId, ArchiveBacklogStoryRequest request, CancellationToken cancellationToken)
    {
        if (IsAdminAuthorized().Not()) return Unauthorized401();
        return await ExecuteAsync(() => _service.ArchiveStoryAsync(storyId, request with { Actor = "cp-admin" }, cancellationToken));
    }

    [HttpPost("stories/{storyId:guid}/unarchive")]
    public async Task<IActionResult> UnarchiveStory(Guid storyId, CancellationToken cancellationToken)
    {
        if (IsAdminAuthorized().Not()) return Unauthorized401();
        return await ExecuteAsync(() => _service.UnarchiveStoryAsync(storyId, "cp-admin", cancellationToken));
    }

    [HttpGet("stories/archived")]
    public async Task<IActionResult> GetArchivedStories(CancellationToken cancellationToken)
    {
        if (IsAdminAuthorized().Not()) return Unauthorized401();
        return Ok(await _service.GetArchivedStoriesAsync(cancellationToken));
    }

    [HttpPost("references/{referenceType}")]
    public async Task<IActionResult> CreateReference(string referenceType, CreateBacklogReferenceRequest request, CancellationToken cancellationToken)
    {
        if (IsAdminAuthorized().Not()) return Unauthorized401();
        return await ExecuteAsync(() => _service.CreateReferenceAsync(referenceType, request with { Actor = "cp-admin" }, cancellationToken));
    }

    [HttpPatch("references/{referenceType}/{key}")]
    public async Task<IActionResult> UpdateReference(string referenceType, string key, UpdateBacklogReferenceRequest request, CancellationToken cancellationToken)
    {
        if (IsAdminAuthorized().Not()) return Unauthorized401();
        return await ExecuteAsync(() => _service.UpdateReferenceAsync(referenceType, key, request with { Actor = "cp-admin" }, cancellationToken));
    }

    [HttpGet("intake")]
    public async Task<IActionResult> GetIntake(CancellationToken cancellationToken)
    {
        if (IsAdminAuthorized().Not()) return Unauthorized401();
        return Ok(await _service.GetIntakeAsync(cancellationToken));
    }

    [HttpPost("intake/{intakeId:guid}/promote")]
    public async Task<IActionResult> PromoteIntake(Guid intakeId, PromoteBacklogIntakeRequest request, CancellationToken cancellationToken)
    {
        if (IsAdminAuthorized().Not()) return Unauthorized401();
        return await ExecuteAsync(() => _service.PromoteIntakeAsync(intakeId, request with { Actor = "cp-admin" }, cancellationToken));
    }

    [HttpGet("stories/{storyId:guid}/history")]
    public async Task<IActionResult> GetHistory(Guid storyId, CancellationToken cancellationToken)
    {
        if (IsAdminAuthorized().Not()) return Unauthorized401();
        return Ok(await _service.GetHistoryAsync(storyId, cancellationToken));
    }

    [HttpPost("export/markdown")]
    public async Task<IActionResult> ExportMarkdown(CancellationToken cancellationToken)
    {
        if (IsAdminAuthorized().Not()) return Unauthorized401();
        var markdown = await _service.ExportMarkdownAsync(cancellationToken);
        return File(System.Text.Encoding.UTF8.GetBytes(markdown), "text/markdown", "backlog-export.md");
    }

    private async Task<IActionResult> ExecuteAsync(Func<Task<BacklogStoryDto>> action)
    {
        try { return Ok(await action()); }
        catch (BacklogConflictException exception) { return Conflict(new { error = exception.Message }); }
        catch (BacklogValidationException exception) { return BadRequest(new { error = exception.Message }); }
    }

    private async Task<IActionResult> ExecuteAsync(Func<Task<BacklogReferenceDto>> action)
    {
        try { return Ok(await action()); }
        catch (BacklogValidationException exception) { return BadRequest(new { error = exception.Message }); }
    }
}
