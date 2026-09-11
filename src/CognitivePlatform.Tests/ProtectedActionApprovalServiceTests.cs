using CognitivePlatform.Api.Automation;
using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Governance;
using Microsoft.Extensions.Options;
using Moq;

namespace CognitivePlatform.Tests;

public class ProtectedActionApprovalServiceTests
{
    private readonly Mock<IObjectStore>             _storeMock = new();
    private readonly ProtectedActionApprovalService _service;

    public ProtectedActionApprovalServiceTests()
    {
        _storeMock.Setup(store => store.Save(It.IsAny<ProtectedActionRequestRecord>()
                                           , It.IsAny<string?>()
                                           , It.IsAny<string?>()))
                  .ReturnsAsync("protected-action-id");
        _storeMock.Setup(store => store.Save(It.IsAny<ProtectedActionApprovalDecision>()
                                           , It.IsAny<string?>()
                                           , It.IsAny<string?>()))
                  .ReturnsAsync("decision-id");
        _service = new ProtectedActionApprovalService(_storeMock.Object, new ProtectedActionClassifier(), Options.Create(new GovernanceSettings()));
    }

    [Fact]
    public async Task RequestAsync_UnclassifiedAction_IsProtectedAndAwaitingApproval()
    {
        var record = await _service.RequestAsync(new ProtectedActionRequest("run-id", "new-operation", EngineeringActionKind.Unclassified));

        Assert.True(record.IsProtected);
        Assert.Equal(ProtectedActionStatus.AwaitingApproval, record.Status);
        Assert.Equal(ProtectedActionCategory.Unclassified, record.Category);
    }

    [Fact]
    public async Task ApproveAsync_RequiredRollbackWithoutPlan_FailsClosed()
    {
        var existing = CreateExistingRecord(EngineeringActionKind.SchemaMigration);
        SetupExisting(existing);

        var exception = await Assert.ThrowsAsync<ProtectedActionPolicyException>(() => _service.ApproveAsync(existing.Id, "Ben", "Approved", null));

        Assert.Contains("rollback", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ApproveAsync_NamedOwnerWithRollbackPlan_RecordsImmutableDecision()
    {
        var existing = CreateExistingRecord(EngineeringActionKind.Deployment);
        SetupExisting(existing);

        var record = await _service.ApproveAsync(existing.Id, "Ben", "Validated deployment evidence", "Restore the previous deployment artifact.");

        Assert.Equal(ProtectedActionStatus.Approved, record.Status);
        Assert.Equal("Ben", record.ApprovedBy);
        _storeMock.Verify(store => store.Save(It.Is<ProtectedActionApprovalDecision>(decision => decision.ActionRequestId == existing.Id
                                                                                      && decision.Decision == ProtectedActionDecision.Approved)
                                           , "protected-action-approval-decisions"
                                           , It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task ApproveAsync_DifferentProductOwner_DeniesApproval()
    {
        var existing = CreateExistingRecord(EngineeringActionKind.SharedComputeAllocation);
        SetupExisting(existing);

        var exception = await Assert.ThrowsAsync<ProtectedActionPolicyException>(() => _service.ApproveAsync(existing.Id, "Someone Else", "Approved", null));

        Assert.Contains("named product owner", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RequireApprovedForExecutionAsync_ActionAwaitingApproval_DeniesExecution()
    {
        var existing = CreateExistingRecord(EngineeringActionKind.ReleasePromotion);
        SetupExisting(existing);

        var exception = await Assert.ThrowsAsync<ProtectedActionPolicyException>(() => _service.RequireApprovedForExecutionAsync(existing.Id));

        Assert.Contains("approval", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static ProtectedActionRequestRecord CreateExistingRecord(EngineeringActionKind actionKind)
    {
        return new ProtectedActionRequestRecord
               {
                   Id          = "protected-action-id"
                 , RunId       = "run-id"
                 , ActionName  = actionKind.ToString()
                 , ActionKind  = actionKind
                 , Category    = ProtectedActionCategory.DeploymentOrRelease
                 , IsProtected = true
                 , RequiresRollbackPlan = true
                 , Status      = ProtectedActionStatus.AwaitingApproval
               };
    }

    private void SetupExisting(ProtectedActionRequestRecord record)
    {
        _storeMock.Setup(store => store.GetAsync<ProtectedActionRequestRecord>(record.Id
                                                                               , "protected-action-requests"
                                                                               , It.IsAny<CancellationToken>()))
                  .ReturnsAsync(record);
    }
}
