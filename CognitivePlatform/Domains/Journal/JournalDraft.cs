namespace CognitivePlatform.Api.Domains.Journal;

public sealed class JournalDraft
{
    public Guid                  Id         { get; init; } = Guid.NewGuid();
    public DateTimeOffset        CreatedUtc { get; init; } = DateTimeOffset.UtcNow;
    public string                Text       { get; init; } = string.Empty;
    public IReadOnlyList<string> Tags       { get; init; } = Array.Empty<string>();
    public string?               Mood       { get; init; }
    public JournalDraftState     State      { get; init; } = JournalDraftState.Local;
    public int?                  MoodScore  { get; set; }
}