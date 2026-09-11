namespace CognitivePlatform.Api.Governance;

public interface ICapabilityMaturityService
{
    Task<CapabilityMaturityRecord> ProposeAsync( string                      capabilityId
                                                , CapabilityMaturityEvidence evidence
                                                , string                      approvedBy
                                                , CancellationToken           cancellationToken = default );

    Task<CapabilityMaturityRecord> PromoteAsync( string                      capabilityId
                                                , CapabilityMaturityLevel    targetLevel
                                                , CapabilityMaturityEvidence evidence
                                                , string                      approvedBy
                                                , CancellationToken           cancellationToken = default );

    Task<CapabilityMaturityRecord> QuarantineAsync( string            capabilityId
                                                   , string            reason
                                                   , string            approvedBy
                                                   , CancellationToken cancellationToken = default );

    Task<CapabilityMaturityRecord?> GetAsync(string capabilityId, CancellationToken cancellationToken = default);
}
