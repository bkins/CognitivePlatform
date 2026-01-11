namespace CognitivePlatform.Api.Domains.Journal;

public sealed class ParsedJournalCommand
{
    public string       Text { get; init; } = string.Empty;
    public List<string> Tags { get; init; } = new();
    public string?      Mood { get; init; }
}
