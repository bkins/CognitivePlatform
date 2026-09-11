namespace CognitivePlatform.Api.Governance;

public sealed record CapabilityMaturityPromotion
{
    public string                  Id           { get; init; } = Guid.NewGuid().ToString("N");
    public string                  CapabilityId { get; init; } = string.Empty;
    public CapabilityMaturityLevel? FromLevel   { get; init; }
    public CapabilityMaturityLevel  ToLevel     { get; init; }
    public string                  ApprovedBy   { get; init; } = string.Empty;
    public DateTimeOffset          OccurredUtc  { get; init; } = DateTimeOffset.UtcNow;
    public string                  Reason       { get; init; } = string.Empty;
}
