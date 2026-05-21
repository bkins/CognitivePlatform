using Moq;
using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Domains.Identity;
using CognitivePlatform.Api.Workspace;

namespace CognitivePlatform.Tests;


public class IdentityServiceTests
{
    private readonly Mock<IObjectStore>      _storeMock            = new();
    private readonly Mock<IWorkspaceContext> _workspaceContextMock = new();
    private readonly IdentityService         _service;

    public IdentityServiceTests()
    {
        _storeMock.Setup(store => store.Save(It.IsAny<PersonProfile>()
                                           , It.IsAny<string?>()
                                           , It.IsAny<string?>()))
                  .ReturnsAsync(string.Empty);

        _storeMock.Setup(store => store.Save(It.IsAny<IdentityAssertion>()
                                           , It.IsAny<string?>()
                                           , It.IsAny<string?>()))
                  .ReturnsAsync(string.Empty);

        _storeMock.Setup(store => store.Save(It.IsAny<DerivedInsight>()
                                           , It.IsAny<string?>()
                                           , It.IsAny<string?>()))
                  .ReturnsAsync(string.Empty);

        _storeMock.Setup(store => store.Save(It.IsAny<PersonalitySnapshot>()
                                           , It.IsAny<string?>()
                                           , It.IsAny<string?>()))
                  .ReturnsAsync(string.Empty);

        _workspaceContextMock.Setup(ctx => ctx.ActivePartitionKey)
                             .Returns((string?)null);

        _service = new IdentityService(_storeMock.Object, _workspaceContextMock.Object);
    }

    // ================================================================
    // GetProfileAsync — singleton seeding
    // ================================================================

    [Fact]
    public async Task GetProfileAsync_ReturnsSeededEmptyProfile_WhenNoProfileExists()
    {
        _storeMock.Setup(store => store.Get<PersonProfile>(IdentityService.GetProfileId(null), null))
                  .Returns((PersonProfile?)null);

        var result = await _service.GetProfileAsync(CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(IdentityService.GetProfileId(null), result.Id);
        Assert.Empty(result.CoreValues);
        Assert.Empty(result.PersonalityTraits);
    }

    [Fact]
    public async Task GetProfileAsync_SavesNewProfile_WhenNoProfileExists()
    {
        _storeMock.Setup(store => store.Get<PersonProfile>(IdentityService.GetProfileId(null), null))
                  .Returns((PersonProfile?)null);

        await _service.GetProfileAsync(CancellationToken.None);

        _storeMock.Verify(store => store.Save(It.IsAny<PersonProfile>()
                                            , It.IsAny<string?>()
                                            , It.IsAny<string?>())
                        , Times.Once);
    }

    [Fact]
    public async Task GetProfileAsync_ReturnsExistingProfile_WithoutSaving()
    {
        var existing = new PersonProfile
                       {
                               Id            = IdentityService.GetProfileId(null)
                             , PreferredName = "Ben"
                       };

        _storeMock.Setup(store => store.Get<PersonProfile>(IdentityService.GetProfileId(null), null))
                  .Returns(existing);

        var result = await _service.GetProfileAsync(CancellationToken.None);

        Assert.Equal("Ben", result.PreferredName);
        _storeMock.Verify(store => store.Save(It.IsAny<PersonProfile>()
                                            , It.IsAny<string?>()
                                            , It.IsAny<string?>())
                        , Times.Never);
    }

    // ================================================================
    // UpdateProfileAsync
    // ================================================================

    [Fact]
    public async Task UpdateProfileAsync_SavesProfileWithSingletonId()
    {
        var profile = new PersonProfile
                      {
                              Id            = IdentityService.GetProfileId(null)
                            , PreferredName = "Ben"
                            , Occupation    = "Engineer"
                      };

        await _service.UpdateProfileAsync(profile, CancellationToken.None);

        _storeMock.Verify(store => store.Save(profile
                                            , null
                                            , IdentityService.GetProfileId(null))
                        , Times.Once);
    }

    // ================================================================
    // AddAssertionAsync
    // ================================================================

    [Fact]
    public async Task AddAssertionAsync_SavesAssertionWithAssertionId()
    {
        var assertionId = Guid.NewGuid().ToString("N");
        var assertion   = new IdentityAssertion
                          {
                                  Id             = assertionId
                                , Topic          = "stress response"
                                , Statement      = "tends to catastrophize timelines"
                                , UserConfirmed  = true
                                , Confidence     = 1.0
                                , FirstObserved  = DateTime.UtcNow
                                , LastReinforced = DateTime.UtcNow
                          };

        await _service.AddAssertionAsync(assertion, CancellationToken.None);

        _storeMock.Verify(store => store.Save(assertion
                                            , null
                                            , assertionId)
                        , Times.Once);
    }

    // ================================================================
    // ConfirmAssertionAsync
    // ================================================================

    [Fact]
    public async Task ConfirmAssertionAsync_SetsUserConfirmedTrue()
    {
        var assertionId = Guid.NewGuid().ToString("N");
        var existing    = new IdentityAssertion
                          {
                                  Id             = assertionId
                                , Topic          = "leadership style"
                                , Statement      = "prefers servant leadership"
                                , UserConfirmed  = false
                                , Confidence     = 0.8
                                , FirstObserved  = DateTime.UtcNow
                                , LastReinforced = DateTime.UtcNow
                          };

        _storeMock.Setup(store => store.Get<IdentityAssertion>(assertionId, null))
                  .Returns(existing);

        await _service.ConfirmAssertionAsync(assertionId, CancellationToken.None);

        _storeMock.Verify(store => store.Save(
                              It.Is<IdentityAssertion>(assertion => assertion.UserConfirmed == true)
                            , null
                            , assertionId)
                        , Times.Once);
    }

    [Fact]
    public async Task ConfirmAssertionAsync_DoesNothing_WhenAssertionNotFound()
    {
        _storeMock.Setup(store => store.Get<IdentityAssertion>(It.IsAny<string>(), null))
                  .Returns((IdentityAssertion?)null);

        await _service.ConfirmAssertionAsync("nonexistent-id", CancellationToken.None);

        _storeMock.Verify(store => store.Save(It.IsAny<IdentityAssertion>()
                                            , It.IsAny<string?>()
                                            , It.IsAny<string?>())
                        , Times.Never);
    }

    [Fact]
    public async Task ConfirmAssertionAsync_DoesNothing_WhenAssertionIsDeleted()
    {
        var assertionId = Guid.NewGuid().ToString("N");
        var deleted     = new IdentityAssertion
                          {
                                  Id        = assertionId
                                , IsDeleted = true
                                , FirstObserved  = DateTime.UtcNow
                                , LastReinforced = DateTime.UtcNow
                          };

        _storeMock.Setup(store => store.Get<IdentityAssertion>(assertionId, null))
                  .Returns(deleted);

        await _service.ConfirmAssertionAsync(assertionId, CancellationToken.None);

        _storeMock.Verify(store => store.Save(It.IsAny<IdentityAssertion>()
                                            , It.IsAny<string?>()
                                            , It.IsAny<string?>())
                        , Times.Never);
    }

    // ================================================================
    // GetAssertionsAsync
    // ================================================================

    [Fact]
    public async Task GetAssertionsAsync_ExcludesDeletedAssertions()
    {
        _storeMock.Setup(store => store.List<IdentityAssertion>(null, null, null))
                  .Returns(
                   [
                           new IdentityAssertion { Id = "a1", Topic = "stress", Statement = "active",  IsDeleted = false, FirstObserved = DateTime.UtcNow, LastReinforced = DateTime.UtcNow }
                         , new IdentityAssertion { Id = "a2", Topic = "stress", Statement = "deleted", IsDeleted = true,  FirstObserved = DateTime.UtcNow, LastReinforced = DateTime.UtcNow }
                   ]);

        var result = await _service.GetAssertionsAsync(CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("active", result[0].Statement);
    }

    [Fact]
    public async Task GetAssertionsAsync_ReturnsEmpty_WhenNoAssertionsExist()
    {
        _storeMock.Setup(store => store.List<IdentityAssertion>(null, null, null))
                  .Returns([]);

        var result = await _service.GetAssertionsAsync(CancellationToken.None);

        Assert.Empty(result);
    }

    // ================================================================
    // AddDerivedInsightAsync
    // ================================================================

    [Fact]
    public async Task AddDerivedInsightAsync_SavesInsightWithInsightId()
    {
        var insightId = Guid.NewGuid().ToString("N");
        var insight   = new DerivedInsight
                        {
                                Id          = insightId
                              , InsightType = "stress-response"
                              , Description = "tends to catastrophize timelines"
                              , Confidence  = 0.75
                        };

        await _service.AddDerivedInsightAsync(insight, CancellationToken.None);

        _storeMock.Verify(store => store.Save(insight
                                            , null
                                            , insightId)
                        , Times.Once);
    }

    // ================================================================
    // ConfirmDerivedInsightAsync
    // ================================================================

    [Fact]
    public async Task ConfirmDerivedInsightAsync_SetsUserConfirmedTrue()
    {
        var insightId = Guid.NewGuid().ToString("N");
        var existing  = new DerivedInsight
                        {
                                Id            = insightId
                              , InsightType   = "leadership-tendency"
                              , Description   = "prefers servant leadership"
                              , UserConfirmed = false
                              , Confidence    = 0.8
                        };

        _storeMock.Setup(store => store.Get<DerivedInsight>(insightId, null))
                  .Returns(existing);

        await _service.ConfirmDerivedInsightAsync(insightId, CancellationToken.None);

        _storeMock.Verify(store => store.Save(
                              It.Is<DerivedInsight>(insight => insight.UserConfirmed == true)
                            , null
                            , insightId)
                        , Times.Once);
    }

    [Fact]
    public async Task ConfirmDerivedInsightAsync_DoesNothing_WhenInsightNotFound()
    {
        _storeMock.Setup(store => store.Get<DerivedInsight>(It.IsAny<string>(), null))
                  .Returns((DerivedInsight?)null);

        await _service.ConfirmDerivedInsightAsync("nonexistent-id", CancellationToken.None);

        _storeMock.Verify(store => store.Save(It.IsAny<DerivedInsight>()
                                            , It.IsAny<string?>()
                                            , It.IsAny<string?>())
                        , Times.Never);
    }

    [Fact]
    public async Task ConfirmDerivedInsightAsync_DoesNothing_WhenInsightIsDeleted()
    {
        var insightId = Guid.NewGuid().ToString("N");
        var deleted   = new DerivedInsight { Id = insightId, IsDeleted = true };

        _storeMock.Setup(store => store.Get<DerivedInsight>(insightId, null))
                  .Returns(deleted);

        await _service.ConfirmDerivedInsightAsync(insightId, CancellationToken.None);

        _storeMock.Verify(store => store.Save(It.IsAny<DerivedInsight>()
                                            , It.IsAny<string?>()
                                            , It.IsAny<string?>())
                        , Times.Never);
    }

    // ================================================================
    // GetDerivedInsightsAsync
    // ================================================================

    [Fact]
    public async Task GetDerivedInsightsAsync_ExcludesDeletedInsights()
    {
        _storeMock.Setup(store => store.List<DerivedInsight>(null, null, null))
                  .Returns(
                   [
                           new DerivedInsight { Id = "i1", InsightType = "active",  Description = "active insight",  IsDeleted = false }
                         , new DerivedInsight { Id = "i2", InsightType = "deleted", Description = "deleted insight", IsDeleted = true  }
                   ]);

        var result = await _service.GetDerivedInsightsAsync(CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("active insight", result[0].Description);
    }

    [Fact]
    public async Task GetDerivedInsightsAsync_ReturnsEmpty_WhenNoInsightsExist()
    {
        _storeMock.Setup(store => store.List<DerivedInsight>(null, null, null))
                  .Returns([]);

        var result = await _service.GetDerivedInsightsAsync(CancellationToken.None);

        Assert.Empty(result);
    }

    // ================================================================
    // AddSnapshotAsync
    // ================================================================

    [Fact]
    public async Task AddSnapshotAsync_SavesSnapshotWithSnapshotId()
    {
        var snapshotId = Guid.NewGuid().ToString("N");
        var snapshot   = new PersonalitySnapshot
                         {
                                 Id               = snapshotId
                               , NarrativeSummary = "A thoughtful engineer."
                         };

        await _service.AddSnapshotAsync(snapshot, CancellationToken.None);

        _storeMock.Verify(store => store.Save(snapshot
                                            , null
                                            , snapshotId)
                        , Times.Once);
    }

    // ================================================================
    // GetLatestSnapshotAsync
    // ================================================================

    [Fact]
    public async Task GetLatestSnapshotAsync_ReturnsNull_WhenNoSnapshotsExist()
    {
        _storeMock.Setup(store => store.List<PersonalitySnapshot>(null, null, null))
                  .Returns([]);

        var result = await _service.GetLatestSnapshotAsync(CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetLatestSnapshotAsync_ReturnsMostRecentSnapshot()
    {
        var older  = new PersonalitySnapshot { Id = "snap1", Timestamp = DateTime.UtcNow.AddDays(-2), NarrativeSummary = "older"  };
        var newest = new PersonalitySnapshot { Id = "snap2", Timestamp = DateTime.UtcNow,              NarrativeSummary = "newest" };

        _storeMock.Setup(store => store.List<PersonalitySnapshot>(null, null, null))
                  .Returns([older, newest]);

        var result = await _service.GetLatestSnapshotAsync(CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("newest", result!.NarrativeSummary);
    }

    // ================================================================
    // GetSnapshotsAsync
    // ================================================================

    [Fact]
    public async Task GetSnapshotsAsync_ExcludesDeletedSnapshots()
    {
        _storeMock.Setup(store => store.List<PersonalitySnapshot>(null, null, null))
                  .Returns(
                   [
                           new PersonalitySnapshot { Id = "s1", NarrativeSummary = "active",  IsDeleted = false }
                         , new PersonalitySnapshot { Id = "s2", NarrativeSummary = "deleted", IsDeleted = true  }
                   ]);

        var result = await _service.GetSnapshotsAsync(CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("active", result[0].NarrativeSummary);
    }
}
