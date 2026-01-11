namespace CognitivePlatform.Api.Domains.Tasks;

public sealed class TaskDto
{
    public Guid                  Id        { get; init; }
    public string                Text      { get; init; } = string.Empty;
    public DateTimeOffset        CreatedAt { get; init; }
    public IReadOnlyList<string> Tags      { get; init; } = Array.Empty<string>();
}
