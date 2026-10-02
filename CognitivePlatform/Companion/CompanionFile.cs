namespace CognitivePlatform.Api.Companion;

public sealed record CompanionFile(string Path, long Bytes, DateTimeOffset ModifiedUtc);
