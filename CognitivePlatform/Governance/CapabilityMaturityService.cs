using CognitivePlatform.Api.Data;
using CP.Shared.Primitives.Avails.Extensions;
using Microsoft.Extensions.Options;

namespace CognitivePlatform.Api.Governance;

public sealed class CapabilityMaturityService : ICapabilityMaturityService
{
    private const string RecordPartitionKey    = "capability-maturity-records";
    private const string PromotionPartitionKey = "capability-maturity-promotions";

    private readonly IObjectStore       _store;
    private readonly GovernanceSettings _settings;

    public CapabilityMaturityService( IObjectStore                    store
                                    , IOptions<GovernanceSettings> settings )
    {
        _store    = store;
        _settings = settings.Value;
    }

    public async Task<CapabilityMaturityRecord> ProposeAsync( string                      capabilityId
                                                              , CapabilityMaturityEvidence evidence
                                                              , string                      approvedBy
                                                              , CancellationToken           cancellationToken = default )
    {
        ValidateCapabilityId(capabilityId);
        EnsureOwner(approvedBy);
        EnsureProposalEvidence(evidence);

        var existing = await GetAsync(capabilityId, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
            throw new CapabilityMaturityPolicyException($"Capability '{capabilityId}' already has a maturity record.");

        var record = new CapabilityMaturityRecord
                     {
                         CapabilityId   = capabilityId.Trim()
                       , Level          = CapabilityMaturityLevel.Proposed
                       , LastApprovedBy = approvedBy.Trim()
                       , UpdatedUtc     = DateTimeOffset.UtcNow
                     };
        await SaveRecordAndHistoryAsync(record, null, approvedBy, "Proposal approved", cancellationToken).ConfigureAwait(false);
        return record;
    }

    public async Task<CapabilityMaturityRecord> PromoteAsync( string                      capabilityId
                                                              , CapabilityMaturityLevel    targetLevel
                                                              , CapabilityMaturityEvidence evidence
                                                              , string                      approvedBy
                                                              , CancellationToken           cancellationToken = default )
    {
        ValidateCapabilityId(capabilityId);
        EnsureOwner(approvedBy);

        var existing = await GetAsync(capabilityId, cancellationToken).ConfigureAwait(false)
                       ?? throw new CapabilityMaturityPolicyException($"Capability '{capabilityId}' has not been proposed.");
        EnsureNextLevel(existing.Level, targetLevel);
        EnsurePromotionEvidence(targetLevel, evidence);

        var record = existing with
                     {
                         Level            = targetLevel
                       , LastApprovedBy   = approvedBy.Trim()
                       , UpdatedUtc       = DateTimeOffset.UtcNow
                       , IsQuarantined    = false
                       , QuarantineReason = null
                     };
        await SaveRecordAndHistoryAsync(record, existing.Level, approvedBy, "Promotion approved", cancellationToken).ConfigureAwait(false);
        return record;
    }

    public async Task<CapabilityMaturityRecord> QuarantineAsync( string            capabilityId
                                                                 , string            reason
                                                                 , string            approvedBy
                                                                 , CancellationToken cancellationToken = default )
    {
        ValidateCapabilityId(capabilityId);
        EnsureOwner(approvedBy);
        if (reason.HasNoValue()) throw new CapabilityMaturityPolicyException("A quarantine reason is required.");

        var existing = await GetAsync(capabilityId, cancellationToken).ConfigureAwait(false)
                       ?? throw new CapabilityMaturityPolicyException($"Capability '{capabilityId}' has not been proposed.");
        if (existing.Level != CapabilityMaturityLevel.Trusted)
            throw new CapabilityMaturityPolicyException("Only Trusted capabilities can be quarantined.");

        var record = existing with
                     {
                         Level            = CapabilityMaturityLevel.Verified
                       , LastApprovedBy   = approvedBy.Trim()
                       , UpdatedUtc       = DateTimeOffset.UtcNow
                       , IsQuarantined    = true
                       , QuarantineReason = reason.Trim()
                     };
        await SaveRecordAndHistoryAsync(record, existing.Level, approvedBy, $"Quarantined: {record.QuarantineReason}", cancellationToken).ConfigureAwait(false);
        return record;
    }

    public Task<CapabilityMaturityRecord?> GetAsync(string capabilityId, CancellationToken cancellationToken = default)
    {
        ValidateCapabilityId(capabilityId);
        return _store.GetAsync<CapabilityMaturityRecord>(capabilityId.Trim(), RecordPartitionKey, cancellationToken);
    }

    private async Task SaveRecordAndHistoryAsync( CapabilityMaturityRecord record
                                                 , CapabilityMaturityLevel? fromLevel
                                                 , string                    approvedBy
                                                 , string                    reason
                                                 , CancellationToken         cancellationToken )
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _store.Save(record, RecordPartitionKey, record.CapabilityId).ConfigureAwait(false);
        var promotion = new CapabilityMaturityPromotion
                        {
                            CapabilityId = record.CapabilityId
                          , FromLevel    = fromLevel
                          , ToLevel      = record.Level
                          , ApprovedBy   = approvedBy.Trim()
                          , Reason       = reason
                        };
        await _store.Save(promotion, PromotionPartitionKey, promotion.Id).ConfigureAwait(false);
    }

    private void EnsureOwner(string approvedBy)
    {
        if (approvedBy.HasNoValue() || approvedBy.Trim().Equals(_settings.NamedProductOwner, StringComparison.OrdinalIgnoreCase).Not())
            throw new CapabilityMaturityPolicyException("Only the configured named product owner may approve capability maturity changes.");
    }

    private static void EnsureNextLevel(CapabilityMaturityLevel currentLevel, CapabilityMaturityLevel targetLevel)
    {
        if ((int)targetLevel != (int)currentLevel + 1)
            throw new CapabilityMaturityPolicyException("Capability maturity must advance one level at a time.");
    }

    private void EnsurePromotionEvidence(CapabilityMaturityLevel targetLevel, CapabilityMaturityEvidence evidence)
    {
        switch (targetLevel)
        {
            case CapabilityMaturityLevel.Implemented:
                if (evidence.BuildSucceeded.Not()) throw new CapabilityMaturityPolicyException("A successful build is required for CML 1.");
                if (evidence.StaticAnalysisPassed.Not()) throw new CapabilityMaturityPolicyException("Static analysis is required for CML 1.");
                if (evidence.BasicDocumentationCreated.Not()) throw new CapabilityMaturityPolicyException("Basic documentation is required for CML 1.");
                break;
            case CapabilityMaturityLevel.Verified:
                if (evidence.UnitTestsPassed.Not()) throw new CapabilityMaturityPolicyException("Passing unit tests are required for CML 2.");
                if (evidence.IntegrationTestsPassed.Not()) throw new CapabilityMaturityPolicyException("Passing integration tests are required for CML 2.");
                if (evidence.UiAutomationPassed.Not()) throw new CapabilityMaturityPolicyException("Passing UI automation is required for CML 2.");
                if (evidence.AcceptanceTestsPassed.Not()) throw new CapabilityMaturityPolicyException("Passing acceptance tests are required for CML 2.");
                if (evidence.RegressionTestsPassed.Not()) throw new CapabilityMaturityPolicyException("Passing regression tests are required for CML 2.");
                if (evidence.ConfigurationValidationPassed.Not()) throw new CapabilityMaturityPolicyException("Configuration validation is required for CML 2.");
                if (evidence.PolicyChecksPassed.Not()) throw new CapabilityMaturityPolicyException("Policy checks are required for CML 2.");
                break;
            case CapabilityMaturityLevel.Trusted:
                if (evidence.ExtendedRegressionPassed.Not()) throw new CapabilityMaturityPolicyException("Extended regression evidence is required for CML 3.");
                if (evidence.ObservabilityPlanDefined.Not()) throw new CapabilityMaturityPolicyException("An observability plan is required for CML 3.");
                if (evidence.RollbackValidated.Not()) throw new CapabilityMaturityPolicyException("Validated rollback is required for CML 3.");
                if (evidence.SuccessfulVerifiedRunCount < _settings.Cml3MinimumVerifiedRunCount)
                    throw new CapabilityMaturityPolicyException($"At least {_settings.Cml3MinimumVerifiedRunCount} successful verified runs are required for CML 3.");
                break;
            default:
                throw new CapabilityMaturityPolicyException("Capabilities must be proposed before they can be promoted.");
        }
    }

    private static void EnsureProposalEvidence(CapabilityMaturityEvidence evidence)
    {
        if (evidence.RequirementsReviewed.Not() || evidence.AcceptanceCriteriaDefined.Not() || evidence.RisksIdentified.Not() || evidence.FeasibilityReviewed.Not())
            throw new CapabilityMaturityPolicyException("Reviewed requirements, acceptance criteria, risks, and feasibility are required for CML 0.");
    }

    private static void ValidateCapabilityId(string capabilityId)
    {
        if (capabilityId.HasNoValue()) throw new CapabilityMaturityPolicyException("A capability identifier is required.");
    }
}
