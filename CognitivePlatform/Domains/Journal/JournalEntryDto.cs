namespace CognitivePlatform.Api.Domains.Journal;

public sealed class JournalEntryDto
{
    public Guid                  Id        { get; init; }
    public string                Text      { get; init; } = string.Empty;
    public DateTimeOffset        CreatedAt { get; init; }
    public IReadOnlyList<string> Tags      { get; init; } = Array.Empty<string>();
}
