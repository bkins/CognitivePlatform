using Moq;
using CognitivePlatform.Api.Domains.Identity;

namespace CognitivePlatform.Tests;

public class IdentityActionsTests
{
    private readonly Mock<IIdentityService> _serviceMock = new();
    private readonly IdentityActions        _actions;

    public IdentityActionsTests()
    {
        _actions = new IdentityActions(_serviceMock.Object);
    }

    // ================================================================
    // GetProfile
    // ================================================================

    [Fact]
    public async Task GetProfile_ReturnsFormattedProfile_WithAllFields()
    {
        var profile = new PersonProfile
                      {
                              Id            = IdentityService.GetProfileId(null)
                            , PreferredName = "Ben"
                            , Occupation    = "Software Engineer"
                            , CoreValues    = ["Integrity", "Excellence"]
                      };

        _serviceMock.Setup(service => service.GetProfileAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(profile);

        var result = await _actions.GetProfile();

        Assert.Contains("Ben",               result);
        Assert.Contains("Software Engineer", result);
        Assert.Contains("Integrity",         result);
        Assert.Contains("Excellence",        result);
    }

    [Fact]
    public async Task GetProfile_ShowsNotSet_ForEmptyScalarFields()
    {
        _serviceMock.Setup(service => service.GetProfileAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new PersonProfile());

        var result = await _actions.GetProfile();

        Assert.Contains("_Not set_", result);
    }

    [Fact]
    public async Task GetProfile_ShowsNone_ForEmptyListFields()
    {
        _serviceMock.Setup(service => service.GetProfileAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new PersonProfile());

        var result = await _actions.GetProfile();

        Assert.Contains("_None_", result);
    }

    // ================================================================
    // SetProfileField
    // ================================================================

    [Fact]
    public async Task SetProfileField_UpdatesPreferredName()
    {
        _serviceMock.Setup(service => service.GetProfileAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new PersonProfile { Id = IdentityService.GetProfileId(null) });

        _serviceMock.Setup(service => service.UpdateProfileAsync(It.IsAny<PersonProfile>(), It.IsAny<CancellationToken>()))
                    .Returns(Task.CompletedTask);

        var result = await _actions.SetProfileField("PreferredName", "Ben");

        _serviceMock.Verify(service => service.UpdateProfileAsync(
                                It.Is<PersonProfile>(profile => profile.PreferredName == "Ben")
                              , It.IsAny<CancellationToken>())
                          , Times.Once);
        Assert.Contains("PreferredName", result);
    }

    [Fact]
    public async Task SetProfileField_UpdatesOccupation()
    {
        _serviceMock.Setup(service => service.GetProfileAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new PersonProfile { Id = IdentityService.GetProfileId(null) });

        _serviceMock.Setup(service => service.UpdateProfileAsync(It.IsAny<PersonProfile>(), It.IsAny<CancellationToken>()))
                    .Returns(Task.CompletedTask);

        await _actions.SetProfileField("Occupation", "Software Engineer");

        _serviceMock.Verify(service => service.UpdateProfileAsync(
                                It.Is<PersonProfile>(profile => profile.Occupation == "Software Engineer")
                              , It.IsAny<CancellationToken>())
                          , Times.Once);
    }

    [Fact]
    public async Task SetProfileField_ReturnsError_ForUnknownField()
    {
        _serviceMock.Setup(service => service.GetProfileAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new PersonProfile());

        var result = await _actions.SetProfileField("UnknownField", "some value");

        Assert.Contains("Unknown field", result);
        _serviceMock.Verify(service => service.UpdateProfileAsync(It.IsAny<PersonProfile>(), It.IsAny<CancellationToken>())
                          , Times.Never);
    }

    [Fact]
    public async Task SetProfileField_AcceptsNameAlias_ForPreferredName()
    {
        _serviceMock.Setup(service => service.GetProfileAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new PersonProfile { Id = IdentityService.GetProfileId(null) });

        _serviceMock.Setup(service => service.UpdateProfileAsync(It.IsAny<PersonProfile>(), It.IsAny<CancellationToken>()))
                    .Returns(Task.CompletedTask);

        await _actions.SetProfileField("name", "Ben");

        _serviceMock.Verify(service => service.UpdateProfileAsync(
                                It.Is<PersonProfile>(profile => profile.PreferredName == "Ben")
                              , It.IsAny<CancellationToken>())
                          , Times.Once);
    }

    // ================================================================
    // AddToProfileList
    // ================================================================

    [Fact]
    public async Task AddToProfileList_AppendsToCoreValues()
    {
        var existing = new PersonProfile { CoreValues = ["Integrity"] };

        _serviceMock.Setup(service => service.GetProfileAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(existing);

        _serviceMock.Setup(service => service.UpdateProfileAsync(It.IsAny<PersonProfile>(), It.IsAny<CancellationToken>()))
                    .Returns(Task.CompletedTask);

        await _actions.AddToProfileList("CoreValues", "Excellence");

        _serviceMock.Verify(service => service.UpdateProfileAsync(
                                It.Is<PersonProfile>(profile => profile.CoreValues.Contains("Excellence")
                                                             && profile.CoreValues.Contains("Integrity"))
                              , It.IsAny<CancellationToken>())
                          , Times.Once);
    }

    [Fact]
    public async Task AddToProfileList_DoesNotAddDuplicate_WhenItemAlreadyPresent()
    {
        var existing = new PersonProfile { CoreValues = ["Integrity"] };

        _serviceMock.Setup(service => service.GetProfileAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(existing);

        _serviceMock.Setup(service => service.UpdateProfileAsync(It.IsAny<PersonProfile>(), It.IsAny<CancellationToken>()))
                    .Returns(Task.CompletedTask);

        await _actions.AddToProfileList("CoreValues", "Integrity");

        _serviceMock.Verify(service => service.UpdateProfileAsync(
                                It.Is<PersonProfile>(profile => profile.CoreValues.Count == 1)
                              , It.IsAny<CancellationToken>())
                          , Times.Once);
    }

    [Fact]
    public async Task AddToProfileList_IsCaseInsensitive_WhenCheckingForDuplicate()
    {
        var existing = new PersonProfile { CoreValues = ["Integrity"] };

        _serviceMock.Setup(service => service.GetProfileAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(existing);

        _serviceMock.Setup(service => service.UpdateProfileAsync(It.IsAny<PersonProfile>(), It.IsAny<CancellationToken>()))
                    .Returns(Task.CompletedTask);

        await _actions.AddToProfileList("CoreValues", "integrity");

        _serviceMock.Verify(service => service.UpdateProfileAsync(
                                It.Is<PersonProfile>(profile => profile.CoreValues.Count == 1)
                              , It.IsAny<CancellationToken>())
                          , Times.Once);
    }

    [Fact]
    public async Task AddToProfileList_ReturnsError_ForUnknownField()
    {
        _serviceMock.Setup(service => service.GetProfileAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new PersonProfile());

        var result = await _actions.AddToProfileList("UnknownField", "value");

        Assert.Contains("Unknown list field", result);
        _serviceMock.Verify(service => service.UpdateProfileAsync(It.IsAny<PersonProfile>(), It.IsAny<CancellationToken>())
                          , Times.Never);
    }

    // ================================================================
    // RemoveFromProfileList
    // ================================================================

    [Fact]
    public async Task RemoveFromProfileList_RemovesMatchingItem()
    {
        var existing = new PersonProfile { Stressors = ["context-switching", "meetings"] };

        _serviceMock.Setup(service => service.GetProfileAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(existing);

        _serviceMock.Setup(service => service.UpdateProfileAsync(It.IsAny<PersonProfile>(), It.IsAny<CancellationToken>()))
                    .Returns(Task.CompletedTask);

        await _actions.RemoveFromProfileList("Stressors", "context-switching");

        _serviceMock.Verify(service => service.UpdateProfileAsync(
                                It.Is<PersonProfile>(profile => profile.Stressors.Count         == 1
                                                             && profile.Stressors.Contains("meetings"))
                              , It.IsAny<CancellationToken>())
                          , Times.Once);
    }

    [Fact]
    public async Task RemoveFromProfileList_IsCaseInsensitive_WhenRemovingItem()
    {
        var existing = new PersonProfile { Strengths = ["Systems thinking", "Empathy"] };

        _serviceMock.Setup(service => service.GetProfileAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(existing);

        _serviceMock.Setup(service => service.UpdateProfileAsync(It.IsAny<PersonProfile>(), It.IsAny<CancellationToken>()))
                    .Returns(Task.CompletedTask);

        await _actions.RemoveFromProfileList("Strengths", "systems thinking");

        _serviceMock.Verify(service => service.UpdateProfileAsync(
                                It.Is<PersonProfile>(profile => profile.Strengths.Count == 1)
                              , It.IsAny<CancellationToken>())
                          , Times.Once);
    }

    [Fact]
    public async Task RemoveFromProfileList_ReturnsError_ForUnknownField()
    {
        _serviceMock.Setup(service => service.GetProfileAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new PersonProfile());

        var result = await _actions.RemoveFromProfileList("UnknownField", "value");

        Assert.Contains("Unknown list field", result);
        _serviceMock.Verify(service => service.UpdateProfileAsync(It.IsAny<PersonProfile>(), It.IsAny<CancellationToken>())
                          , Times.Never);
    }

    // ================================================================
    // AddIdentityAssertion
    // ================================================================

    [Fact]
    public async Task AddIdentityAssertion_SetsConfidenceToOne_AndUserConfirmedTrue()
    {
        _serviceMock.Setup(service => service.AddAssertionAsync(It.IsAny<IdentityAssertion>(), It.IsAny<CancellationToken>()))
                    .Returns(Task.CompletedTask);

        await _actions.AddIdentityAssertion("stress response", "tends to catastrophize timelines");

        _serviceMock.Verify(service => service.AddAssertionAsync(
                                It.Is<IdentityAssertion>(assertion => assertion.Confidence    == 1.0
                                                                   && assertion.UserConfirmed == true
                                                                   && assertion.Topic         == "stress response"
                                                                   && assertion.Statement     == "tends to catastrophize timelines")
                              , It.IsAny<CancellationToken>())
                          , Times.Once);
    }

    [Fact]
    public async Task AddIdentityAssertion_AssignsNonEmptyId()
    {
        IdentityAssertion? captured = null;

        _serviceMock.Setup(service => service.AddAssertionAsync(It.IsAny<IdentityAssertion>(), It.IsAny<CancellationToken>()))
                    .Callback<IdentityAssertion, CancellationToken>((assertion, _) => captured = assertion)
                    .Returns(Task.CompletedTask);

        await _actions.AddIdentityAssertion("topic", "statement");

        Assert.NotNull(captured);
        Assert.NotEmpty(captured!.Id);
    }

    [Fact]
    public async Task AddIdentityAssertion_SetsFirstObservedAndLastReinforced()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);

        _serviceMock.Setup(service => service.AddAssertionAsync(It.IsAny<IdentityAssertion>(), It.IsAny<CancellationToken>()))
                    .Returns(Task.CompletedTask);

        await _actions.AddIdentityAssertion("topic", "statement");

        _serviceMock.Verify(service => service.AddAssertionAsync(
                                It.Is<IdentityAssertion>(assertion => assertion.FirstObserved  >= before
                                                                   && assertion.LastReinforced >= before)
                              , It.IsAny<CancellationToken>())
                          , Times.Once);
    }

    // ================================================================
    // ListIdentityAssertions
    // ================================================================

    [Fact]
    public async Task ListIdentityAssertions_ReturnsEmptyMessage_WhenNoAssertions()
    {
        _serviceMock.Setup(service => service.GetAssertionsAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync([]);

        var result = await _actions.ListIdentityAssertions();

        Assert.Contains("No identity assertions", result);
    }

    [Fact]
    public async Task ListIdentityAssertions_GroupsByTopic_AndIncludesStatements()
    {
        _serviceMock.Setup(service => service.GetAssertionsAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(
                     [
                             new IdentityAssertion { Topic = "stress",     Statement = "catastrophizes",  UserConfirmed = true,  FirstObserved = DateTime.UtcNow, LastReinforced = DateTime.UtcNow }
                           , new IdentityAssertion { Topic = "leadership", Statement = "servant style",   UserConfirmed = false, FirstObserved = DateTime.UtcNow, LastReinforced = DateTime.UtcNow }
                     ]);

        var result = await _actions.ListIdentityAssertions();

        Assert.Contains("stress",         result);
        Assert.Contains("leadership",     result);
        Assert.Contains("catastrophizes", result);
        Assert.Contains("servant style",  result);
    }

    [Fact]
    public async Task ListIdentityAssertions_MarksConfirmedAssertions()
    {
        _serviceMock.Setup(service => service.GetAssertionsAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(
                     [
                             new IdentityAssertion { Topic = "personality", Statement = "introvert", UserConfirmed = true, FirstObserved = DateTime.UtcNow, LastReinforced = DateTime.UtcNow }
                     ]);

        var result = await _actions.ListIdentityAssertions();

        Assert.Contains("[confirmed]", result);
    }

    // ================================================================
    // ConfirmIdentityAssertion
    // ================================================================

    [Fact]
    public async Task ConfirmIdentityAssertion_ConfirmsMostRecentAssertionForTopic()
    {
        var newerAssertion = new IdentityAssertion
                             {
                                     Id             = "newer-id"
                                   , Topic          = "leadership"
                                   , Statement      = "servant leadership"
                                   , UserConfirmed  = false
                                   , FirstObserved  = DateTime.UtcNow
                                   , LastReinforced = DateTime.UtcNow
                             };
        var olderAssertion = new IdentityAssertion
                             {
                                     Id             = "older-id"
                                   , Topic          = "leadership"
                                   , Statement      = "older statement"
                                   , UserConfirmed  = false
                                   , FirstObserved  = DateTime.UtcNow.AddDays(-1)
                                   , LastReinforced = DateTime.UtcNow.AddDays(-1)
                             };

        _serviceMock.Setup(service => service.GetAssertionsAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync([newerAssertion, olderAssertion]);

        _serviceMock.Setup(service => service.ConfirmAssertionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .Returns(Task.CompletedTask);

        await _actions.ConfirmIdentityAssertion("leadership");

        _serviceMock.Verify(service => service.ConfirmAssertionAsync("newer-id", It.IsAny<CancellationToken>())
                          , Times.Once);
    }

    [Fact]
    public async Task ConfirmIdentityAssertion_IsTopicCaseInsensitive()
    {
        var assertion = new IdentityAssertion
                        {
                                Id             = "assert-id"
                              , Topic          = "Stress Response"
                              , Statement      = "catastrophizes"
                              , UserConfirmed  = false
                              , FirstObserved  = DateTime.UtcNow
                              , LastReinforced = DateTime.UtcNow
                        };

        _serviceMock.Setup(service => service.GetAssertionsAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync([assertion]);

        _serviceMock.Setup(service => service.ConfirmAssertionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .Returns(Task.CompletedTask);

        await _actions.ConfirmIdentityAssertion("stress response");

        _serviceMock.Verify(service => service.ConfirmAssertionAsync("assert-id", It.IsAny<CancellationToken>())
                          , Times.Once);
    }

    [Fact]
    public async Task ConfirmIdentityAssertion_ReturnsNotFound_WhenTopicHasNoAssertions()
    {
        _serviceMock.Setup(service => service.GetAssertionsAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync([]);

        var result = await _actions.ConfirmIdentityAssertion("unknown topic");

        Assert.Contains("No assertion found", result);
        _serviceMock.Verify(service => service.ConfirmAssertionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())
                          , Times.Never);
    }
}
