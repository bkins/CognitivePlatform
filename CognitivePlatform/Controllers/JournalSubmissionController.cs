using System.Security.Cryptography;
using System.Text;
using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Domains.Journal;
using Microsoft.AspNetCore.Mvc;

namespace CognitivePlatform.Api.Controllers;

[ApiController]
[Route("api/journals/submissions")]
public sealed class JournalSubmissionController(SqliteObjectStore store, IConfiguration configuration) : ControllerBase
{
    [HttpPut("{submissionId:guid}")]
    [RequestSizeLimit(1_048_576)]
    public ActionResult<JournalSubmissionReceipt> Submit(Guid                     submissionId
                                                      , JournalSubmissionRequest request)
    {
        var configuredHash = configuration["JournalSubmission:WriteKeySha256"];
        if (configuredHash is null || configuredHash.Length != 64 || !configuredHash.All(Uri.IsHexDigit))
            return NotFound();

        var keys = Request.Headers["X-Cp-Journal-Write-Key"];
        if (keys.Count != 1 || string.IsNullOrWhiteSpace(keys[0]))
            return Unauthorized();

        var suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(keys[0]!));
        if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(configuredHash), suppliedHash))
            return Unauthorized();

        if (submissionId == Guid.Empty || request.AuthoredAt == default || string.IsNullOrWhiteSpace(request.Text))
            return BadRequest("Submission ID, authored timestamp and journal text are required.");

        var receipt = store.SubmitJournal(submissionId, request);
        if (receipt is null)
            return Conflict("This submission ID already belongs to a different journal snapshot.");

        return Ok(receipt);
    }
}
