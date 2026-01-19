namespace CognitivePlatform.Api.Models.TestingTemp;

public sealed class JournalEditTestRequest
{
    public string Text { get; init; } = string.Empty;

    public IReadOnlyList<string>? Tags      { get; init; }
    public string?                Mood      { get; init; }
    public int?                   MoodScore { get; init; }
}
