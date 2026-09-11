namespace CognitivePlatform.Api.Governance;

public sealed record CapabilityMaturityApprovalRequest(
    CapabilityMaturityEvidence Evidence,
    string                     ApprovedBy);
