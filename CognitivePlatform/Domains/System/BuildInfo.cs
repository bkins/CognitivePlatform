
namespace CognitivePlatform.Api.Domains.System;

public class BuildInfo
{
    public string Version { get; init; } = string.Empty;

    public string InformationalVersion { get; init; } = string.Empty;

    public string BuildConfiguration { get; init; } = string.Empty;

    public DateTimeOffset? BuildTimeUtc { get; init; }

    public string? CommitSha { get; init; }

    public string? Branch { get; init; }
}