namespace CognitivePlatform.Api.Automation;

public interface IProtectedActionApprovalService
{
    Task<ProtectedActionRequestRecord> RequestAsync(ProtectedActionRequest request, CancellationToken cancellationToken = default);
    Task<ProtectedActionRequestRecord?> GetAsync(string actionRequestId, CancellationToken cancellationToken = default);
    Task<ProtectedActionRequestRecord> ApproveAsync(string actionRequestId, string approvedBy, string reason, string? rollbackPlan, CancellationToken cancellationToken = default);
    Task<ProtectedActionRequestRecord> RequireApprovedForExecutionAsync(string actionRequestId, CancellationToken cancellationToken = default);
}
