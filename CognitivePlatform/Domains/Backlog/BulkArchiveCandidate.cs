namespace CognitivePlatform.Api.Domains.Backlog;

public sealed record BulkArchiveCandidate(
    Guid           StoryId,
    string         DisplayId,
    string         Title,
    long           ExpectedRevision,
    DateTimeOffset CompletedAtUtc);
