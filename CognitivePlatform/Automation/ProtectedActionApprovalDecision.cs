namespace CognitivePlatform.Api.Automation;

public sealed record ProtectedActionApprovalDecision
{
    public string                   Id              { get; init; } = Guid.NewGuid().ToString("N");
    public string                   ActionRequestId { get; init; } = string.Empty;
    public ProtectedActionDecision  Decision        { get; init; }
    public string                   ApprovedBy      { get; init; } = string.Empty;
    public string                   Reason          { get; init; } = string.Empty;
    public string?                  RollbackPlan    { get; init; }
    public DateTimeOffset           DecidedUtc      { get; init; } = DateTimeOffset.UtcNow;
}
