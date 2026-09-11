namespace CognitivePlatform.Api.Governance;

public sealed record CapabilityMaturityQuarantineRequest(
    string Reason,
    string ApprovedBy);
