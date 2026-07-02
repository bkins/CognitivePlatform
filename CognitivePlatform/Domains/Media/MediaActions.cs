using System.Text;
using CognitivePlatform.Api.Attributes;
using CognitivePlatform.Api.Domains.Journal.Interfaces;
using CognitivePlatform.Api.Domains.Tasks;
using CognitivePlatform.Api.Registry.Domains;

namespace CognitivePlatform.Api.Domains.Media;

[Domain(typeof(MediaDomain))]
public sealed class MediaActions
{
    private readonly IMediaAttachmentService _service;
    private readonly IJournalService?         _journalService;
    private readonly ITaskService?            _taskService;

    public MediaActions(IMediaAttachmentService service
                      , IJournalService? journalService = null
                      , ITaskService? taskService = null)
    {
        _service        = service;
        _journalService = journalService;
        _taskService    = taskService;
    }

    private string ResolveOwnerReference(string ownerType, string ownerId)
    {
        if (string.Equals(ownerType, "JournalEntry", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ownerType, "Journal", StringComparison.OrdinalIgnoreCase))
        {
            if (_journalService is not null)
            {
                var ordered = _journalService.GetOrderedEntries();
                var match = ordered.FirstOrDefault(e => e.EntryWithRevision.Entry.Id == ownerId);
                if (match != default)
                {
                    return $"journal entry #{match.Position}";
                }
            }
        }
        else if (string.Equals(ownerType, "Task", StringComparison.OrdinalIgnoreCase))
        {
            if (_taskService is not null)
            {
                var ordered = _taskService.GetOrderedActiveTasks();
                var match = ordered.FirstOrDefault(e => e.Task.Id == ownerId);
                if (match != default)
                {
                    return $"task #{match.Position}";
                }
            }
        }

        // Fallback
        return $"{ownerType} {ownerId}";
    }

    [NaturalLanguageAction(
        Description = "Lists all media attachments for a given owner (e.g., a journal entry or task)."
      , Examples    = new[]
                      {
                          "list attachments for journal entry abc123"
                        , "show media for task xyz"
                        , "what files are attached to this entry"
                        , "show attachments"
                      }
      , Category    = "media")]
    public async Task<string> ListAttachments(
        [NaturalLanguageParam(Description = "The owner type, e.g. JournalEntry or Task.")]
        string ownerType,
        [NaturalLanguageParam(Description = "The ID of the owner item.")]
        string ownerId)
    {
        var ownerLabel  = ResolveOwnerReference(ownerType, ownerId);
        var attachments = await _service.GetAttachmentsAsync(ownerType, ownerId);

        if (attachments.Count == 0)
            return $"No attachments found for {ownerLabel}.";

        var sb = new StringBuilder();
        sb.AppendLine($"Attachments for {ownerLabel} ({attachments.Count}):");
        sb.AppendLine();

        for (var i = 0; i < attachments.Count; i++)
        {
            var attachment = attachments[i];
            var sizeKb = attachment.FileSizeBytes / 1024.0;
            sb.AppendLine($"• #{i + 1}: {attachment.FileName} ({attachment.ContentType}, {sizeKb:F1} KB)");
        }

        return sb.ToString().TrimEnd();
    }

    [NaturalLanguageAction(
        Description = "Returns the number of media attachments for a given owner."
      , Examples    = new[]
                      {
                          "how many attachments does journal entry abc123 have"
                        , "attachment count for task xyz"
                        , "count media files for entry abc"
                      }
      , Category    = "media")]
    public async Task<string> GetAttachmentCount(
        [NaturalLanguageParam(Description = "The owner type, e.g. JournalEntry or Task.")]
        string ownerType,
        [NaturalLanguageParam(Description = "The ID of the owner item.")]
        string ownerId)
    {
        var ownerLabel = ResolveOwnerReference(ownerType, ownerId);
        var count = await _service.GetAttachmentCountAsync(ownerType, ownerId);

        return count switch
        {
            0 => $"No attachments for {ownerLabel}."
          , 1 => $"1 attachment for {ownerLabel}."
          , _ => $"{count} attachments for {ownerLabel}."
        };
    }
}
