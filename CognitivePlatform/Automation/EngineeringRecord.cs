namespace CognitivePlatform.Api.Automation;

public sealed record EngineeringRecord
{
    public string                   Id           { get; init; } = Guid.NewGuid().ToString("N");
    public string                   CapabilityId { get; init; } = string.Empty;
    public string                   RequestSummary { get; init; } = string.Empty;
    public string                   BaseRevision { get; init; } = string.Empty;
    public EngineeringRunStatus     Status       { get; init; }
    public EngineeringTaskGraph     TaskGraph    { get; init; } = EngineeringTaskGraph.CreateDefault();
    public EngineeringExecutionUsage Usage       { get; init; } = new(TimeSpan.Zero, 0, 0, 0m);
    public string?                  WorkspacePath { get; init; }
    public string?                  StopReason  { get; init; }
    public DateTimeOffset           CreatedUtc  { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset           UpdatedUtc  { get; init; } = DateTimeOffset.UtcNow;
}
