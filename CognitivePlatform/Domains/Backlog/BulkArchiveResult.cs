namespace CognitivePlatform.Api.Domains.Backlog;

public sealed record BulkArchiveResult(IReadOnlyList<BacklogStoryDto> ArchivedStories);
