namespace CognitivePlatform.Api.Automation;

public interface IEngineeringPipelineService
{
    Task<EngineeringRecord> CreateAsync(CreateEngineeringRunRequest request, CancellationToken cancellationToken = default);
    Task<EngineeringRecord?> GetAsync(string engineeringRunId, CancellationToken cancellationToken = default);
    Task<EngineeringRecord> ProvisionWorkspaceAsync(string engineeringRunId, CancellationToken cancellationToken = default);
    Task<EngineeringRecord> RecordUsageAsync(string engineeringRunId, EngineeringExecutionUsage usage, CancellationToken cancellationToken = default);
}
