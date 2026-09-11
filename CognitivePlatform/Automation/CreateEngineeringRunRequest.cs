namespace CognitivePlatform.Api.Automation;

public sealed record CreateEngineeringRunRequest(
    string CapabilityId,
    string RequestSummary,
    string BaseRevision);
