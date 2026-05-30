namespace CognitivePlatform.Api.Integrations.FileSync.Models;

public sealed record FileConflict
{
    public string         RelativePath        { get; init; } = string.Empty;
    public DateTimeOffset SourceModified      { get; init; }
    public DateTimeOffset DestinationModified { get; init; }
}
