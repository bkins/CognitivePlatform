using CognitivePlatform.Api.Domains.Media;
using Microsoft.AspNetCore.Mvc;

namespace CognitivePlatform.Api.Controllers;

[ApiController]
[Route("api/media")]
public sealed class MediaController : ControllerBase
{
    private readonly IMediaAttachmentService _mediaService;

    public MediaController(IMediaAttachmentService mediaService)
    {
        _mediaService = mediaService;
    }

    // POST /api/media/{ownerType}/{ownerId}
    [HttpPost("{ownerType}/{ownerId:guid}")]
    public async Task<ActionResult<MediaAttachmentDto>> Upload(string    ownerType
                                                             , Guid      ownerId
                                                             , IFormFile file)
    {
        if (file is null || file.Length == 0)
            return BadRequest("No file provided.");

        await using var stream = file.OpenReadStream();

        var attachment = await _mediaService.AddAttachmentAsync(ownerType
                                                              , ownerId.ToString("N")
                                                              , file.FileName
                                                              , file.ContentType ?? "application/octet-stream"
                                                              , stream
                                                              , file.Length);

        var dto = ToDto(attachment);
        return CreatedAtAction(nameof(GetById), new { id = attachment.Id }, dto);
    }

    // GET /api/media/{ownerType}/{ownerId}
    [HttpGet("{ownerType}/{ownerId:guid}")]
    public async Task<ActionResult<IReadOnlyList<MediaAttachmentDto>>> List(string ownerType, Guid ownerId)
    {
        var attachments = await _mediaService.GetAttachmentsAsync(ownerType, ownerId.ToString("N"));
        return Ok(attachments.Select(ToDto).ToList());
    }

    // GET /api/media/{id}
    [HttpGet("{id}")]
    public async Task<ActionResult<MediaAttachmentDto>> GetById(string id)
    {
        var attachment = await _mediaService.GetAttachmentAsync(id);
        if (attachment is null || attachment.IsDeleted)
            return NotFound();

        return Ok(ToDto(attachment));
    }

    // GET /api/media/{id}/file
    [HttpGet("{id}/file")]
    public async Task<IActionResult> DownloadFile(string id)
    {
        var attachment = await _mediaService.GetAttachmentAsync(id);
        if (attachment is null || attachment.IsDeleted)
            return NotFound();

        var stream = await _mediaService.GetAttachmentStreamAsync(id);
        if (stream is null)
            return NotFound("File not found on storage.");

        return File(stream, attachment.ContentType, attachment.FileName);
    }

    // DELETE /api/media/{id}
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var attachment = await _mediaService.GetAttachmentAsync(id);
        if (attachment is null || attachment.IsDeleted)
            return NotFound();

        await _mediaService.DeleteAttachmentAsync(id);
        return NoContent();
    }

    private static MediaAttachmentDto ToDto(MediaAttachment attachment)
        => new()
           {
               Id            = new Guid(attachment.Id)
             , OwnerType     = attachment.OwnerType
             , OwnerId       = new Guid(attachment.OwnerId)
             , FileName      = attachment.FileName
             , ContentType   = attachment.ContentType
             , FileSizeBytes = attachment.FileSizeBytes
             , CreatedAt     = attachment.CreatedAt
           };
}
