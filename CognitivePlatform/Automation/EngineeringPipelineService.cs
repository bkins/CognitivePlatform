using CognitivePlatform.Api.Data;
using CP.Shared.Primitives.Avails.Extensions;
using Microsoft.Extensions.Options;

namespace CognitivePlatform.Api.Automation;

public sealed class EngineeringPipelineService : IEngineeringPipelineService
{
    private const string RecordPartitionKey = "engineering-records";
    private const string EventPartitionKey  = "engineering-record-events";

    private readonly IObjectStore                   _store;
    private readonly IEngineeringWorkspaceProvisioner _workspaceProvisioner;
    private readonly EngineeringPipelineSettings    _settings;

    public EngineeringPipelineService( IObjectStore                     store
                                     , IEngineeringWorkspaceProvisioner workspaceProvisioner
                                     , IOptions<EngineeringPipelineSettings> settings )
    {
        _store                = store;
        _workspaceProvisioner = workspaceProvisioner;
        _settings             = settings.Value;
    }

    public async Task<EngineeringRecord> CreateAsync(CreateEngineeringRunRequest request, CancellationToken cancellationToken = default)
    {
        if (request.CapabilityId.HasNoValue()) throw new EngineeringPipelineException("A capability identifier is required.");
        if (request.RequestSummary.HasNoValue()) throw new EngineeringPipelineException("A request summary is required.");
        if (request.BaseRevision.HasNoValue()) throw new EngineeringPipelineException("A base revision is required.");

        var record = new EngineeringRecord
                     {
                         CapabilityId   = request.CapabilityId.Trim()
                       , RequestSummary = request.RequestSummary.Trim()
                       , BaseRevision   = request.BaseRevision.Trim()
                       , Status         = EngineeringRunStatus.Planned
                     };
        await SaveAsync(record, "Run planned with a deterministic task graph.", cancellationToken).ConfigureAwait(false);
        return record;
    }

    public Task<EngineeringRecord?> GetAsync(string engineeringRunId, CancellationToken cancellationToken = default)
    {
        ValidateRunId(engineeringRunId);
        return _store.GetAsync<EngineeringRecord>(engineeringRunId, RecordPartitionKey, cancellationToken);
    }

    public async Task<EngineeringRecord> ProvisionWorkspaceAsync(string engineeringRunId, CancellationToken cancellationToken = default)
    {
        var existing = await GetRequiredAsync(engineeringRunId, cancellationToken).ConfigureAwait(false);
        if (existing.Status != EngineeringRunStatus.Planned)
            throw new EngineeringPipelineException("Only planned runs may provision a workspace.");

        var workspace = await _workspaceProvisioner.ProvisionAsync(existing.Id, existing.BaseRevision, cancellationToken).ConfigureAwait(false);
        var record = existing with
                     {
                         Status        = EngineeringRunStatus.WorkspaceReady
                       , WorkspacePath = workspace.Path
                       , UpdatedUtc    = DateTimeOffset.UtcNow
                     };
        await SaveAsync(record, "Disposable workspace provisioned.", cancellationToken).ConfigureAwait(false);
        return record;
    }

    public async Task<EngineeringRecord> RecordUsageAsync( string                    engineeringRunId
                                                           , EngineeringExecutionUsage usage
                                                           , CancellationToken         cancellationToken = default )
    {
        var existing = await GetRequiredAsync(engineeringRunId, cancellationToken).ConfigureAwait(false);
        var stopReason = GetBudgetStopReason(usage);
        var record = existing with
                     {
                         Usage      = usage
                       , Status     = stopReason is null ? existing.Status : EngineeringRunStatus.PausedForReview
                       , StopReason = stopReason
                       , UpdatedUtc = DateTimeOffset.UtcNow
                     };
        await SaveAsync(record, stopReason ?? "Execution usage recorded.", cancellationToken).ConfigureAwait(false);
        return record;
    }

    private async Task<EngineeringRecord> GetRequiredAsync(string engineeringRunId, CancellationToken cancellationToken)
    {
        return await GetAsync(engineeringRunId, cancellationToken).ConfigureAwait(false)
               ?? throw new EngineeringPipelineException($"Engineering run '{engineeringRunId}' was not found.");
    }

    private async Task SaveAsync(EngineeringRecord record, string detail, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _store.Save(record, RecordPartitionKey, record.Id).ConfigureAwait(false);
        var auditEvent = new EngineeringRecordEvent
                         {
                             EngineeringRunId = record.Id
                           , Action           = record.Status.ToString()
                           , Detail           = detail
                         };
        await _store.Save(auditEvent, EventPartitionKey, auditEvent.Id).ConfigureAwait(false);
    }

    private string? GetBudgetStopReason(EngineeringExecutionUsage usage)
    {
        if (usage.Elapsed > TimeSpan.FromMinutes(_settings.MaxElapsedMinutes)) return "Elapsed-time budget exceeded.";
        if (usage.ModelRequestCount > _settings.MaxModelRequests) return "Model-request budget exceeded.";
        if (usage.RepairAttemptCount > _settings.MaxRepairAttempts) return "Repair-attempt budget exceeded.";
        if (usage.SpendUsd > _settings.MaxSpendUsd) return "Spend budget exceeded.";
        return null;
    }

    private static void ValidateRunId(string engineeringRunId)
    {
        if (engineeringRunId.HasNoValue()) throw new EngineeringPipelineException("An engineering run identifier is required.");
    }
}
