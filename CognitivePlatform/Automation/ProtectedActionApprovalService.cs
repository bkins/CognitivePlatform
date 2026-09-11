using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Governance;
using CP.Shared.Primitives.Avails.Extensions;
using Microsoft.Extensions.Options;

namespace CognitivePlatform.Api.Automation;

public sealed class ProtectedActionApprovalService : IProtectedActionApprovalService
{
    private const string RequestPartitionKey  = "protected-action-requests";
    private const string DecisionPartitionKey = "protected-action-approval-decisions";

    private readonly IObjectStore              _store;
    private readonly ProtectedActionClassifier _classifier;
    private readonly GovernanceSettings        _settings;

    public ProtectedActionApprovalService( IObjectStore                     store
                                          , ProtectedActionClassifier        classifier
                                          , IOptions<GovernanceSettings> settings )
    {
        _store      = store;
        _classifier = classifier;
        _settings   = settings.Value;
    }

    public async Task<ProtectedActionRequestRecord> RequestAsync(ProtectedActionRequest request, CancellationToken cancellationToken = default)
    {
        if (request.RunId.HasNoValue()) throw new ProtectedActionPolicyException("An engineering run identifier is required.");
        if (request.ActionName.HasNoValue()) throw new ProtectedActionPolicyException("A protected-action name is required.");

        var classification = _classifier.Classify(request.ActionKind);
        var record = new ProtectedActionRequestRecord
                     {
                         RunId                = request.RunId.Trim()
                       , ActionName           = request.ActionName.Trim()
                       , ActionKind           = request.ActionKind
                       , Category             = classification.Category
                       , IsProtected          = classification.IsProtected
                       , RequiresRollbackPlan = classification.RequiresRollbackPlan
                       , Status               = classification.IsProtected ? ProtectedActionStatus.AwaitingApproval : ProtectedActionStatus.NotRequired
                     };
        await _store.Save(record, RequestPartitionKey, record.Id).ConfigureAwait(false);
        return record;
    }

    public Task<ProtectedActionRequestRecord?> GetAsync(string actionRequestId, CancellationToken cancellationToken = default)
    {
        ValidateActionRequestId(actionRequestId);
        return _store.GetAsync<ProtectedActionRequestRecord>(actionRequestId.Trim(), RequestPartitionKey, cancellationToken);
    }

    public async Task<ProtectedActionRequestRecord> ApproveAsync( string            actionRequestId
                                                                   , string            approvedBy
                                                                   , string            reason
                                                                   , string?           rollbackPlan
                                                                   , CancellationToken cancellationToken = default )
    {
        var existing = await GetRequiredAsync(actionRequestId, cancellationToken).ConfigureAwait(false);
        if (existing.IsProtected.Not()) throw new ProtectedActionPolicyException("This sandbox action does not require protected-action approval.");
        if (existing.Status != ProtectedActionStatus.AwaitingApproval) throw new ProtectedActionPolicyException("This protected action has already received an approval decision.");
        EnsureOwner(approvedBy);
        if (reason.HasNoValue()) throw new ProtectedActionPolicyException("An approval reason is required.");
        if (existing.RequiresRollbackPlan && rollbackPlan.HasNoValue()) throw new ProtectedActionPolicyException("A rollback plan is required before approving this protected action.");

        var record = existing with
                     {
                         Status         = ProtectedActionStatus.Approved
                       , ApprovedBy     = approvedBy.Trim()
                       , ApprovalReason = reason.Trim()
                       , RollbackPlan   = rollbackPlan?.Trim()
                       , UpdatedUtc     = DateTimeOffset.UtcNow
                     };
        await _store.Save(record, RequestPartitionKey, record.Id).ConfigureAwait(false);
        var decision = new ProtectedActionApprovalDecision
                       {
                           ActionRequestId = record.Id
                         , Decision        = ProtectedActionDecision.Approved
                         , ApprovedBy      = record.ApprovedBy
                         , Reason          = record.ApprovalReason
                         , RollbackPlan    = record.RollbackPlan
                       };
        await _store.Save(decision, DecisionPartitionKey, decision.Id).ConfigureAwait(false);
        return record;
    }

    public async Task<ProtectedActionRequestRecord> RequireApprovedForExecutionAsync(string actionRequestId, CancellationToken cancellationToken = default)
    {
        var record = await GetRequiredAsync(actionRequestId, cancellationToken).ConfigureAwait(false);
        if (record.IsProtected && record.Status != ProtectedActionStatus.Approved)
            throw new ProtectedActionPolicyException("Explicit named-owner approval is required before executing this protected action.");
        if (record.RequiresRollbackPlan && record.RollbackPlan.HasNoValue())
            throw new ProtectedActionPolicyException("A rollback plan is required before executing this protected action.");
        return record;
    }

    private async Task<ProtectedActionRequestRecord> GetRequiredAsync(string actionRequestId, CancellationToken cancellationToken)
    {
        return await GetAsync(actionRequestId, cancellationToken).ConfigureAwait(false)
               ?? throw new ProtectedActionPolicyException($"Protected action '{actionRequestId}' was not found.");
    }

    private void EnsureOwner(string approvedBy)
    {
        if (approvedBy.HasNoValue() || approvedBy.Trim().Equals(_settings.NamedProductOwner, StringComparison.OrdinalIgnoreCase).Not())
            throw new ProtectedActionPolicyException("Only the configured named product owner may approve protected actions.");
    }

    private static void ValidateActionRequestId(string actionRequestId)
    {
        if (actionRequestId.HasNoValue()) throw new ProtectedActionPolicyException("A protected-action request identifier is required.");
    }
}
