namespace CognitivePlatform.Api.Domains.Backlog;

public sealed record BulkArchiveSelection(
    Guid StoryId,
    long ExpectedRevision);
