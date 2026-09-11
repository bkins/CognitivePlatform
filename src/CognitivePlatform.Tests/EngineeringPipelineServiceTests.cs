using CognitivePlatform.Api.Automation;
using CognitivePlatform.Api.Data;
using Microsoft.Extensions.Options;
using Moq;

namespace CognitivePlatform.Tests;

public class EngineeringPipelineServiceTests
{
    private readonly Mock<IObjectStore>               _storeMock = new();
    private readonly Mock<IEngineeringWorkspaceProvisioner> _workspaceProvisionerMock = new();
    private readonly EngineeringPipelineService       _service;

    public EngineeringPipelineServiceTests()
    {
        _storeMock.Setup(store => store.Save(It.IsAny<EngineeringRecord>()
                                           , It.IsAny<string?>()
                                           , It.IsAny<string?>()))
                  .ReturnsAsync("run-id");
        _storeMock.Setup(store => store.Save(It.IsAny<EngineeringRecordEvent>()
                                           , It.IsAny<string?>()
                                           , It.IsAny<string?>()))
                  .ReturnsAsync("event-id");
        _workspaceProvisionerMock.Setup(provisioner => provisioner.ProvisionAsync(It.IsAny<string>()
                                                                                   , It.IsAny<string>()
                                                                                   , It.IsAny<CancellationToken>()))
                                  .ReturnsAsync(new EngineeringWorkspace("run-id", "C:\\CP\\Data\\Dev\\EngineeringWorkspaces\\run-id", "abc123"));

        var settings = Options.Create(new EngineeringPipelineSettings
                                      {
                                          MaxElapsedMinutes = 30
                                        , MaxModelRequests  = 20
                                        , MaxRepairAttempts = 3
                                        , MaxSpendUsd       = 0m
                                      });
        _service = new EngineeringPipelineService(_storeMock.Object, _workspaceProvisionerMock.Object, settings);
    }

    [Fact]
    public async Task CreateAsync_CreatesReviewableSandboxRunWithDeterministicTaskGraph()
    {
        var record = await _service.CreateAsync(new CreateEngineeringRunRequest("fixture-capability", "Add a non-destructive fixture", "abc123"));

        Assert.Equal(EngineeringRunStatus.Planned, record.Status);
        Assert.Equal("fixture-capability", record.CapabilityId);
        Assert.Contains(record.TaskGraph.Tasks, task => task.Kind == EngineeringTaskKind.Requirements);
        Assert.Contains(record.TaskGraph.Tasks, task => task.Kind == EngineeringTaskKind.PromotionReview);
        Assert.Null(record.WorkspacePath);
    }

    [Fact]
    public async Task RecordUsageAsync_OverBudget_PausesForReviewWithoutExecutingFurtherWork()
    {
        var existing = CreateExistingRecord();
        SetupExisting(existing);

        var record = await _service.RecordUsageAsync(existing.Id, new EngineeringExecutionUsage(TimeSpan.FromMinutes(31), 0, 0, 0m));

        Assert.Equal(EngineeringRunStatus.PausedForReview, record.Status);
        Assert.Contains("elapsed", record.StopReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProvisionWorkspaceAsync_UsesIsolatedProvisionerAndRecordsWorkspace()
    {
        var existing = CreateExistingRecord();
        SetupExisting(existing);

        var record = await _service.ProvisionWorkspaceAsync(existing.Id);

        Assert.Equal(EngineeringRunStatus.WorkspaceReady, record.Status);
        Assert.Equal("C:\\CP\\Data\\Dev\\EngineeringWorkspaces\\run-id", record.WorkspacePath);
        _workspaceProvisionerMock.Verify(provisioner => provisioner.ProvisionAsync(existing.Id, existing.BaseRevision, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static EngineeringRecord CreateExistingRecord()
    {
        return new EngineeringRecord
               {
                   Id           = "run-id"
                 , CapabilityId = "fixture-capability"
                 , BaseRevision = "abc123"
                 , Status       = EngineeringRunStatus.Planned
                 , TaskGraph    = EngineeringTaskGraph.CreateDefault()
               };
    }

    private void SetupExisting(EngineeringRecord record)
    {
        _storeMock.Setup(store => store.GetAsync<EngineeringRecord>(record.Id
                                                                   , "engineering-records"
                                                                   , It.IsAny<CancellationToken>()))
                  .ReturnsAsync(record);
    }
}
