namespace CognitivePlatform.Api.Companion;

public sealed record CompanionDocument(string Path, string ContentHash, DateTimeOffset ReadUtc, Guid? DocumentId, string Markdown, string Html, CompanionMetadata? Metadata = null);
