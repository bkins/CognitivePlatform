using Moq;
using CognitivePlatform.Api.Workspace;

namespace CognitivePlatform.Tests;

/// <summary>
/// Tests for WorkspaceContext (KnownWorkspaces registry, normalization, persistence)
/// and WorkspaceActions (UseWorkspace, GetCurrentWorkspace, ListWorkspaces).
/// </summary>
public class WorkspaceTests
{
    // ================================================================
    // WorkspaceContext — construction
    // ================================================================

    private static Mock<IUserSettingsService> BuildSettingsService(
        string  activeWorkspace = WorkspaceKeys.Personal,
        string[]? knownWorkspaces = null)
    {
        var settings = new UserSettings
        {
            ActiveWorkspace = activeWorkspace,
            KnownWorkspaces = knownWorkspaces ?? [WorkspaceKeys.Personal]
        };

        var mock = new Mock<IUserSettingsService>();
        mock.Setup(userSettingsService => userSettingsService.Get()).Returns(settings);
        mock.Setup(userSettingsService => userSettingsService.SaveAsync(It.IsAny<UserSettings>()))
            .Returns(Task.CompletedTask);
        return mock;
    }

    [Fact]
    public void Constructor_InitializesActiveWorkspace_FromSettings()
    {
        var settingsMock = BuildSettingsService(activeWorkspace: "work",
                                               knownWorkspaces: ["personal", "work"]);

        var context = new WorkspaceContext(settingsMock.Object);

        Assert.Equal("work", context.ActiveWorkspace);
    }

    [Fact]
    public void Constructor_InitializesKnownWorkspaces_FromSettings()
    {
        var settingsMock = BuildSettingsService(activeWorkspace: "personal",
                                               knownWorkspaces: ["personal", "work", "family"]);

        var context = new WorkspaceContext(settingsMock.Object);

        Assert.Equal(["personal", "work", "family"], context.KnownWorkspaces);
    }

    [Fact]
    public void Constructor_FallsBackToPersonal_WhenSettingsKnownWorkspacesIsEmpty()
    {
        var settingsMock = BuildSettingsService(activeWorkspace: "personal",
                                               knownWorkspaces: []);

        var context = new WorkspaceContext(settingsMock.Object);

        Assert.Equal([WorkspaceKeys.Personal], context.KnownWorkspaces);
    }

    // ================================================================
    // WorkspaceContext — ActivePartitionKey
    // ================================================================

    [Fact]
    public void ActivePartitionKey_ReturnsNull_ForPersonalWorkspace()
    {
        var settingsMock = BuildSettingsService(activeWorkspace: WorkspaceKeys.Personal);

        var context = new WorkspaceContext(settingsMock.Object);

        Assert.Null(context.ActivePartitionKey);
    }

    [Fact]
    public void ActivePartitionKey_ReturnsWorkspaceName_ForNonPersonalWorkspace()
    {
        var settingsMock = BuildSettingsService(activeWorkspace: "work",
                                               knownWorkspaces: ["personal", "work"]);

        var context = new WorkspaceContext(settingsMock.Object);

        Assert.Equal("work", context.ActivePartitionKey);
    }

    // ================================================================
    // WorkspaceContext — SetActiveWorkspaceAsync
    // ================================================================

    [Fact]
    public async Task SetActiveWorkspaceAsync_UpdatesActiveWorkspace()
    {
        var settingsMock = BuildSettingsService(activeWorkspace: "personal");
        var context = new WorkspaceContext(settingsMock.Object);

        await context.SetActiveWorkspaceAsync("work");

        Assert.Equal("work", context.ActiveWorkspace);
    }

    [Fact]
    public async Task SetActiveWorkspaceAsync_AppendsNewWorkspace_ToKnownWorkspaces()
    {
        var settingsMock = BuildSettingsService(activeWorkspace: "personal",
                                               knownWorkspaces: ["personal"]);
        var context = new WorkspaceContext(settingsMock.Object);

        await context.SetActiveWorkspaceAsync("work");

        Assert.Contains("work", context.KnownWorkspaces);
        Assert.Equal(2, context.KnownWorkspaces.Count);
    }

    [Fact]
    public async Task SetActiveWorkspaceAsync_DoesNotDuplicate_ExistingWorkspace()
    {
        var settingsMock = BuildSettingsService(activeWorkspace: "personal",
                                               knownWorkspaces: ["personal", "work"]);
        var context = new WorkspaceContext(settingsMock.Object);

        await context.SetActiveWorkspaceAsync("work");

        Assert.Equal(2, context.KnownWorkspaces.Count);
    }

    [Fact]
    public async Task SetActiveWorkspaceAsync_NormalisesWorkspaceName_ToLowercase()
    {
        var settingsMock = BuildSettingsService(activeWorkspace: "personal");
        var context = new WorkspaceContext(settingsMock.Object);

        await context.SetActiveWorkspaceAsync("  WORK  ");

        Assert.Equal("work", context.ActiveWorkspace);
        Assert.Contains("work", context.KnownWorkspaces);
    }

    [Fact]
    public async Task SetActiveWorkspaceAsync_PersistsUpdatedSettings()
    {
        var settingsMock = BuildSettingsService(activeWorkspace: "personal",
                                               knownWorkspaces: ["personal"]);
        var context = new WorkspaceContext(settingsMock.Object);

        await context.SetActiveWorkspaceAsync("work");

        settingsMock.Verify(
            userSettingsService => userSettingsService.SaveAsync(
                It.Is<UserSettings>(settings =>
                    settings.ActiveWorkspace == "work" &&
                    settings.KnownWorkspaces.Contains("work"))),
            Times.Once);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SetActiveWorkspaceAsync_Throws_WhenNameIsNullOrWhitespace(string? name)
    {
        var settingsMock = BuildSettingsService();
        var context = new WorkspaceContext(settingsMock.Object);

        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => context.SetActiveWorkspaceAsync(name!));
    }

    // ================================================================
    // WorkspaceActions — UseWorkspace
    // ================================================================

    [Fact]
    public async Task UseWorkspace_ReturnsSwitchedMessage_WhenWorkspaceChanges()
    {
        var ctx = new Mock<IWorkspaceContext>();
        ctx.SetupSequence(workspaceContext => workspaceContext.ActiveWorkspace)
           .Returns("personal")
           .Returns("work");
        ctx.Setup(workspaceContext => workspaceContext.SetActiveWorkspaceAsync("work"))
           .Returns(Task.CompletedTask);

        var actions = new WorkspaceActions(ctx.Object);

        var result = await actions.UseWorkspace("work");

        Assert.Equal("Switched to workspace: work", result);
    }

    [Fact]
    public async Task UseWorkspace_ReturnsAlreadyInMessage_WhenWorkspaceUnchanged()
    {
        var ctx = new Mock<IWorkspaceContext>();
        ctx.Setup(workspaceContext => workspaceContext.ActiveWorkspace).Returns("work");
        ctx.Setup(workspaceContext => workspaceContext.SetActiveWorkspaceAsync("work"))
           .Returns(Task.CompletedTask);

        var actions = new WorkspaceActions(ctx.Object);

        var result = await actions.UseWorkspace("work");

        Assert.Equal("Already in workspace: work", result);
    }

    // ================================================================
    // WorkspaceActions — GetCurrentWorkspace
    // ================================================================

    [Fact]
    public void GetCurrentWorkspace_ReturnsFormattedActiveWorkspaceName()
    {
        var ctx = new Mock<IWorkspaceContext>();
        ctx.Setup(workspaceContext => workspaceContext.ActiveWorkspace).Returns("family");

        var actions = new WorkspaceActions(ctx.Object);

        var result = actions.GetCurrentWorkspace();

        Assert.Equal("Active workspace: family", result);
    }

    // ================================================================
    // WorkspaceActions — ListWorkspaces
    // ================================================================

    [Fact]
    public void ListWorkspaces_MarksOnlyWorkspace_AsActive_WhenSingleEntry()
    {
        var ctx = new Mock<IWorkspaceContext>();
        ctx.Setup(workspaceContext => workspaceContext.ActiveWorkspace).Returns("personal");
        ctx.Setup(workspaceContext => workspaceContext.KnownWorkspaces)
           .Returns(["personal"]);

        var actions = new WorkspaceActions(ctx.Object);

        var result = actions.ListWorkspaces();

        Assert.Equal("Workspaces: personal (active)", result);
    }

    [Fact]
    public void ListWorkspaces_MarksCorrectWorkspace_AsActive_WithMultipleWorkspaces()
    {
        var ctx = new Mock<IWorkspaceContext>();
        ctx.Setup(workspaceContext => workspaceContext.ActiveWorkspace).Returns("work");
        ctx.Setup(workspaceContext => workspaceContext.KnownWorkspaces)
           .Returns(["personal", "work", "family"]);

        var actions = new WorkspaceActions(ctx.Object);

        var result = actions.ListWorkspaces();

        Assert.Equal("Workspaces: personal, work (active), family", result);
    }

    [Fact]
    public void ListWorkspaces_MarksActive_CaseInsensitively()
    {
        var ctx = new Mock<IWorkspaceContext>();
        ctx.Setup(workspaceContext => workspaceContext.ActiveWorkspace).Returns("WORK");
        ctx.Setup(workspaceContext => workspaceContext.KnownWorkspaces)
           .Returns(["personal", "work"]);

        var actions = new WorkspaceActions(ctx.Object);

        var result = actions.ListWorkspaces();

        Assert.Contains("work (active)", result);
    }
}
