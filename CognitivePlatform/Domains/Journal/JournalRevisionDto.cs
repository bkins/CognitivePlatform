namespace CognitivePlatform.Api.Domains.Journal;

public sealed class JournalRevisionDto
{
    public Guid           RevisionId { get; init; }
    public DateTimeOffset CreatedAt  { get; init; }

    public string                Text      { get; init; } = string.Empty;
    public IReadOnlyList<string> Tags      { get; init; } = Array.Empty<string>();
    public string?               Mood      { get; init; }
    public int?                  MoodScore { get; init; }
}