using CognitivePlatform.Api.Companion;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CognitivePlatform.Api.Controllers;

[ApiController]
[Route("api/companion")]
[TypeFilter(typeof(CompanionAuthorizationFilter))]
[RequestSizeLimit(4096)]
public sealed class CompanionController(IOptionsMonitor<CompanionSettings> settings, CompanionWorkspaceReader reader, CompanionRequestGate gate) : ControllerBase
{
    [HttpGet("workspaces")]
    public IActionResult Workspaces()
        => Ok(new { protocolVersion = 1, server = "CP Development read-only companion", savedDocumentsOnly = true
                  , workspaces = settings.CurrentValue.Workspaces.Take(16).Select(workspace => new { workspace.Id, workspace.Label }) });

    [HttpGet("files")]
    public Task<IActionResult> Files(string workspaceId, CancellationToken cancellationToken)
        => GuardAsync(async token =>
        {
            var workspace = Find(workspaceId);
            var files = reader.List(workspace, token);
            await Task.CompletedTask;
            return Ok(new { workspaceId, readUtc = DateTimeOffset.UtcNow, files });
        }, cancellationToken);

    [HttpGet("document")]
    public Task<IActionResult> Document(string workspaceId, string path, CancellationToken cancellationToken)
        => GuardAsync(async token =>
        {
            var workspace = Find(workspaceId);
            var document = await reader.ReadAsync(workspace, path, token);
            document = document with { Metadata = await new CompanionMetadataReader().ReadAsync(workspace, path, document.DocumentId, token) };
            Response.Headers.ETag = '"' + document.ContentHash + '"';
            return Ok(document);
        }, cancellationToken);

    [HttpPost("search")]
    public Task<IActionResult> Search(CompanionSearchRequest request, CancellationToken cancellationToken)
        => GuardAsync(async token =>
        {
            if (string.IsNullOrWhiteSpace(request.Query) || request.Query.Length > 512 || request.Offset is < 0 or > 1000)
                return BadRequest(new { code = "invalid_search" });
            var workspace = Find(request.WorkspaceId);
            var files = reader.List(workspace, token);
            var batch = files.Skip(request.Offset).Take(128).ToArray();
            var matches = new List<object>();
            long bytes = 0;
            var scanned = 0;
            foreach (var file in batch)
            {
                token.ThrowIfCancellationRequested();
                if (bytes + file.Bytes > 8 * 1_048_576 && scanned > 0) break;
                bytes += file.Bytes;
                scanned++;
                var document = await reader.ReadAsync(workspace, file.Path, token);
                var offset = document.Markdown.IndexOf(request.Query, StringComparison.OrdinalIgnoreCase);
                if (offset < 0) continue;
                var start = Math.Max(0, offset - 60);
                matches.Add(new { file.Path, document.ContentHash, lineNumber = document.Markdown.AsSpan(0, offset).Count('\n') + 1
                                , excerpt = document.Markdown.Substring(start, Math.Min(240, document.Markdown.Length - start)) });
            }
            int? nextOffset = request.Offset + scanned < files.Count ? request.Offset + scanned : null;
            return Ok(new { request.WorkspaceId, readUtc = DateTimeOffset.UtcNow, matches, scanned, totalFiles = files.Count, nextOffset });
        }, cancellationToken);

    private CompanionWorkspace Find(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 64) throw new UnauthorizedAccessException();
        var matches = settings.CurrentValue.Workspaces.Take(16).Where(workspace => workspace.Id == id).ToArray();
        return matches.Length == 1 && Path.IsPathFullyQualified(matches[0].RootPath) ? matches[0] : throw new UnauthorizedAccessException();
    }

    private async Task<IActionResult> GuardAsync(Func<CancellationToken, Task<IActionResult>> operation, CancellationToken cancellationToken)
    {
        if (!await gate.Semaphore.WaitAsync(0, cancellationToken)) return StatusCode(429, new { code = "companion_busy" });
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try { return await operation(timeout.Token); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return StatusCode(504, new { code = "companion_timeout" }); }
        catch (UnauthorizedAccessException) { return StatusCode(403, new { code = "workspace_or_path_not_shared" }); }
        catch (IOException) { return StatusCode(503, new { code = "shared_source_unavailable" }); }
        catch (ArgumentException) { return BadRequest(new { code = "invalid_read_request" }); }
        catch (InvalidOperationException) { return StatusCode(413, new { code = "companion_bound_exceeded" }); }
        finally { gate.Semaphore.Release(); }
    }
}
