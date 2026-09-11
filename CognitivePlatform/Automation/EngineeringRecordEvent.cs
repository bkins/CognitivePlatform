namespace CognitivePlatform.Api.Automation;

public sealed record EngineeringRecordEvent
{
    public string          Id          { get; init; } = Guid.NewGuid().ToString("N");
    public string          EngineeringRunId { get; init; } = string.Empty;
    public string          Action      { get; init; } = string.Empty;
    public string          Detail      { get; init; } = string.Empty;
    public DateTimeOffset  OccurredUtc { get; init; } = DateTimeOffset.UtcNow;
}
