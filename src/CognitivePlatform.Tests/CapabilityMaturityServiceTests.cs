using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Governance;
using Microsoft.Extensions.Options;
using Moq;

namespace CognitivePlatform.Tests;

public class CapabilityMaturityServiceTests
{
    private readonly Mock<IObjectStore>          _storeMock = new();
    private readonly CapabilityMaturityService    _service;

    public CapabilityMaturityServiceTests()
    {
        _storeMock.Setup(store => store.Save(It.IsAny<CapabilityMaturityRecord>()
                                           , It.IsAny<string?>()
                                           , It.IsAny<string?>()))
                  .ReturnsAsync("record-id");
        _storeMock.Setup(store => store.Save(It.IsAny<CapabilityMaturityPromotion>()
                                           , It.IsAny<string?>()
                                           , It.IsAny<string?>()))
                  .ReturnsAsync("promotion-id");

        var settings = Options.Create(new GovernanceSettings
                                      {
                                          NamedProductOwner             = "Ben"
                                        , Cml3MinimumVerifiedRunCount = 3
                                      });
        _service = new CapabilityMaturityService(_storeMock.Object, settings);
    }

    [Fact]
    public async Task ProposeAsync_WithOwnerAndCompleteProposalEvidence_CreatesCmlZeroAndImmutableHistory()
    {
        var evidence = CapabilityMaturityEvidence.ForProposal();

        var record = await _service.ProposeAsync("fixture-capability", evidence, "Ben");

        Assert.Equal(CapabilityMaturityLevel.Proposed, record.Level);
        _storeMock.Verify(store => store.Save(It.IsAny<CapabilityMaturityPromotion>()
                                             , "capability-maturity-promotions"
                                             , It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task PromoteAsync_WithNonOwner_RejectsPromotion()
    {
        var existing = new CapabilityMaturityRecord
                       {
                           CapabilityId = "fixture-capability"
                         , Level        = CapabilityMaturityLevel.Proposed
                       };
        SetupExisting(existing);

        var exception = await Assert.ThrowsAsync<CapabilityMaturityPolicyException>(() =>
            _service.PromoteAsync("fixture-capability", CapabilityMaturityLevel.Implemented, CapabilityMaturityEvidence.ForImplementation(), "Not-Ben"));

        Assert.Contains("named product owner", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PromoteAsync_ToVerified_WithFailedRequiredValidation_RejectsPromotion()
    {
        var existing = new CapabilityMaturityRecord
                       {
                           CapabilityId = "fixture-capability"
                         , Level        = CapabilityMaturityLevel.Implemented
                       };
        SetupExisting(existing);
        var evidence = CapabilityMaturityEvidence.ForVerified() with { UnitTestsPassed = false };

        var exception = await Assert.ThrowsAsync<CapabilityMaturityPolicyException>(() =>
            _service.PromoteAsync("fixture-capability", CapabilityMaturityLevel.Verified, evidence, "Ben"));

        Assert.Contains("unit tests", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PromoteAsync_ToTrusted_WithRequiredEvidence_PromotesAndRecordsApproval()
    {
        var existing = new CapabilityMaturityRecord
                       {
                           CapabilityId = "fixture-capability"
                         , Level        = CapabilityMaturityLevel.Verified
                       };
        SetupExisting(existing);

        var record = await _service.PromoteAsync("fixture-capability", CapabilityMaturityLevel.Trusted, CapabilityMaturityEvidence.ForTrusted(3), "Ben");

        Assert.Equal(CapabilityMaturityLevel.Trusted, record.Level);
        Assert.Equal("Ben", record.LastApprovedBy);
        _storeMock.Verify(store => store.Save(It.IsAny<CapabilityMaturityPromotion>()
                                             , "capability-maturity-promotions"
                                             , It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task QuarantineAsync_TrustedCapability_DemotesToVerifiedAndRecordsReason()
    {
        var existing = new CapabilityMaturityRecord
                       {
                           CapabilityId = "fixture-capability"
                         , Level        = CapabilityMaturityLevel.Trusted
                       };
        SetupExisting(existing);

        var record = await _service.QuarantineAsync("fixture-capability", "Regression failed", "Ben");

        Assert.Equal(CapabilityMaturityLevel.Verified, record.Level);
        Assert.Equal("Regression failed", record.QuarantineReason);
    }

    private void SetupExisting(CapabilityMaturityRecord record)
    {
        _storeMock.Setup(store => store.GetAsync<CapabilityMaturityRecord>(record.CapabilityId
                                                                           , "capability-maturity-records"
                                                                           , It.IsAny<CancellationToken>()))
                  .ReturnsAsync(record);
    }
}
