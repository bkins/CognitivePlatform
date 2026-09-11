namespace CognitivePlatform.Api.Automation;

public sealed record ProtectedActionApprovalRequest(
    string  ApprovedBy,
    string  Reason,
    string? RollbackPlan);
