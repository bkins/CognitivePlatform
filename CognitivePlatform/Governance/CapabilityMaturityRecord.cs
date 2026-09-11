namespace CognitivePlatform.Api.Governance;

public sealed record CapabilityMaturityRecord
{
    public string                  CapabilityId   { get; init; } = string.Empty;
    public CapabilityMaturityLevel Level          { get; init; }
    public string                  LastApprovedBy { get; init; } = string.Empty;
    public DateTimeOffset          UpdatedUtc     { get; init; } = DateTimeOffset.UtcNow;
    public bool                    IsQuarantined  { get; init; }
    public string?                 QuarantineReason { get; init; }
}
