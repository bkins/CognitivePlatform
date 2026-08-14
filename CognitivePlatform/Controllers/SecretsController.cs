using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CognitivePlatform.Api.Contracts;
using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Domains.Journal;
using CognitivePlatform.Api.Domains.Journal.Interfaces;
using CognitivePlatform.Api.Domains.Secrets;
using CognitivePlatform.Api.Domains.Tasks;
using CP.Shared.Primitives.Avails.Extensions;
using Microsoft.AspNetCore.Mvc;

namespace CognitivePlatform.Api.Controllers;

[ApiController]
[Route("api/secrets")]
public sealed class SecretsController : ControllerBase
{
    private readonly ISecretVaultService _vault;
    private readonly IObjectStore        _store;
    private readonly IJournalService     _journalService;
    private readonly ITaskService        _taskService;

    public SecretsController(
        ISecretVaultService vault
      , IObjectStore store
      , IJournalService journalService
      , ITaskService taskService)
    {
        _vault          = vault ?? throw new ArgumentNullException(nameof(vault));
        _store          = store ?? throw new ArgumentNullException(nameof(store));
        _journalService = journalService ?? throw new ArgumentNullException(nameof(journalService));
        _taskService    = taskService ?? throw new ArgumentNullException(nameof(taskService));
    }

    [HttpPost("setup")]
    public async Task<IActionResult> Setup([FromBody] VaultPinRequest request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Pin))
        {
            return BadRequest("PIN is required.");
        }

        var success = await _vault.SetupAsync(request.Pin).ConfigureAwait(false);
        if (success)
        {
            return Ok(new { Success = true, Message = "Secrets vault initialized successfully." });
        }

        return BadRequest("Failed to initialize secrets vault.");
    }

    [HttpPost("unlock")]
    public async Task<IActionResult> Unlock([FromBody] VaultPinRequest request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Pin))
        {
            return BadRequest("PIN is required.");
        }

        var success = await _vault.UnlockAsync(request.Pin).ConfigureAwait(false);
        if (success)
        {
            return Ok(new { Success = true, Message = "Secrets vault unlocked." });
        }

        return BadRequest("Incorrect PIN.");
    }

    [HttpPost("lock")]
    public IActionResult Lock()
    {
        _vault.Lock();
        return Ok(new { Success = true, Message = "Secrets vault locked." });
    }

    [HttpGet("status")]
    public IActionResult GetStatus()
    {
        return Ok(new
                  {
                      IsInitialized = _vault.IsInitialized()
                    , IsUnlocked    = _vault.IsUnlocked()
                  });
    }

    [HttpPost("archive-inbox-item")]
    public async Task<IActionResult> ArchiveInboxItem([FromBody] ArchiveInboxItemRequest request, CancellationToken ct)
    {
        if (request is null)
        {
            return BadRequest("Request is required.");
        }

        if (!_vault.IsUnlocked())
        {
            return Ok(new ConverseResponse
                      {
                          Success                = false
                        , IsVaultUnlockRequired = true
                        , Message                = "Secrets vault is locked. Please unlock to archive this item."
                      });
        }

        var itemIdString = request.ItemId.ToString("N");
        string textToEncrypt;
        string title;
        var category = request.Kind;

        if (request.Kind.EqualsIgnoreCase("Journal"))
        {
            var entry = _journalService.GetEntry(itemIdString);
            if (entry is null)
            {
                return NotFound("Journal entry not found.");
            }

            var revisions = _journalService.GetRevisionHistory(itemIdString);
            var latestRevision = revisions.OrderByDescending(rev => rev.CreatedUtc)
                                          .FirstOrDefault();
            if (latestRevision is null)
            {
                return NotFound("Journal revision not found.");
            }

            textToEncrypt = latestRevision.Text;
            title         = latestRevision.Text.Length <= 60 ? latestRevision.Text : latestRevision.Text[..57] + "…";

            _journalService.DeleteEntry(itemIdString, "Archived to Secrets Vault");
        }
        else if (request.Kind.EqualsIgnoreCase("Task"))
        {
            var task = _taskService.Get(itemIdString);
            if (task is null)
            {
                return NotFound("Task not found.");
            }

            textToEncrypt = $"Task: {task.ShortDescription}\nDetails: {task.Details}";
            title         = task.ShortDescription;

            _taskService.Delete(itemIdString);
        }
        else
        {
            return BadRequest("Unsupported item kind for server archiving.");
        }

        var (payload, nonce, tag) = await _vault.EncryptAsync(textToEncrypt).ConfigureAwait(false);
        var secretEntry = new SecretEntry
                          {
                              Id               = Guid.NewGuid().ToString("N")
                            , Title            = title
                            , Category         = category
                            , EncryptedPayload = payload
                            , Nonce            = nonce
                            , AuthTag          = tag
                            , CreatedUtc       = DateTimeOffset.UtcNow
                          };

        await _store.Save(secretEntry, id: secretEntry.Id).ConfigureAwait(false);

        return Ok(new ConverseResponse
                  {
                      Success = true
                    , Message = $"Successfully moved {request.Kind} item to Secrets Vault."
                  });
    }
}

public record VaultPinRequest(string Pin);

public record ArchiveInboxItemRequest(Guid ItemId, string Kind);
