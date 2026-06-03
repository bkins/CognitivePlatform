using System.Text;
using CognitivePlatform.Api.Attributes;
using CognitivePlatform.Api.Registry.Domains;

namespace CognitivePlatform.Api.Domains.Media;

[Domain(typeof(MediaDomain))]
public sealed class MediaActions
{
    private readonly IMediaAttachmentService _service;

    public MediaActions(IMediaAttachmentService service)
    {
        _service = service;
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
        var attachments = await _service.GetAttachmentsAsync(ownerType, ownerId);

        if (attachments.Count == 0)
            return $"No attachments found for {ownerType} {ownerId}.";

        var sb = new StringBuilder();
        sb.AppendLine($"Attachments for {ownerType} {ownerId} ({attachments.Count}):");
        sb.AppendLine();

        foreach (var attachment in attachments)
        {
            var sizeKb = attachment.FileSizeBytes / 1024.0;
            sb.AppendLine($"• {attachment.FileName} ({attachment.ContentType}, {sizeKb:F1} KB)  —  ID: {attachment.Id}");
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
        var count = await _service.GetAttachmentCountAsync(ownerType, ownerId);

        return count switch
        {
            0 => $"No attachments for {ownerType} {ownerId}."
          , 1 => $"1 attachment for {ownerType} {ownerId}."
          , _ => $"{count} attachments for {ownerType} {ownerId}."
        };
    }
}
