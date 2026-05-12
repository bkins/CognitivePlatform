using Moq;
using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Workspace;

namespace CognitivePlatform.Tests;

// ============================================================
// WorkspaceKeys
// ============================================================

public class WorkspaceKeysTests
{
    [Theory]
    [InlineData("personal")]
    [InlineData("Personal")]
    [InlineData("PERSONAL")]
    public void ToPartitionKey_ReturnsNull_ForPersonalWorkspace(string workspaceName)
    {
        var result = WorkspaceKeys.ToPartitionKey(workspaceName);

        Assert.Null(result);
    }

    [Theory]
    [InlineData("work",     "work")]
    [InlineData("family",   "family")]
    [InlineData("side",     "side")]
    [InlineData("Work",     "Work")]   // ToPartitionKey does NOT normalise case — WorkspaceContext does
    public void ToPartitionKey_ReturnsName_ForNonPersonalWorkspace(string workspaceName, string expected)
    {
        var result = WorkspaceKeys.ToPartitionKey(workspaceName);

        Assert.Equal(expected, result);
    }
}

// ============================================================
// UserSettings
// ============================================================

public class UserSettingsTests
{
    [Fact]
    public void DefaultUserSettings_HasPersonalWorkspace()
    {
        var settings = new UserSettings();

        Assert.Equal(WorkspaceKeys.Personal, settings.ActiveWorkspace);
    }

    [Fact]
    public void DefaultUserSettings_HasCorrectSettingsId()
    {
        var settings = new UserSettings();

        Assert.Equal(UserSettingsService.SettingsId, settings.Id);
    }
}

// ============================================================
// UserSettingsService
// ============================================================

public class UserSettingsServiceTests
{
    private readonly Mock<IObjectStore>  _storeMock = new();
    private readonly UserSettingsService _service;

    public UserSettingsServiceTests()
    {
        _storeMock.Setup(store => store.Save(It.IsAny<UserSettings>()
                                           , It.IsAny<string?>()
                                           , It.IsAny<string?>()))
                  .ReturnsAsync(UserSettingsService.SettingsId);

        _service = new UserSettingsService(_storeMock.Object);
    }

    [Fact]
    public void Get_ReturnsDefault_WhenNoRecordExists()
    {
        _storeMock.Setup(store => store.Get<UserSettings>(UserSettingsService.SettingsId, null))
                  .Returns((UserSettings?)null);

        var result = _service.Get();

        Assert.NotNull(result);
        Assert.Equal(WorkspaceKeys.Personal, result.ActiveWorkspace);
    }

    [Fact]
    public void Get_ReturnsPersistedSettings_WhenRecordExists()
    {
        var persisted = new UserSettings { ActiveWorkspace = "work" };
        _storeMock.Setup(store => store.Get<UserSettings>(UserSettingsService.SettingsId, null))
                  .Returns(persisted);

        var result = _service.Get();

        Assert.Equal("work", result.ActiveWorkspace);
    }

    [Fact]
    public async Task SaveAsync_CallsStore_WithCorrectIdAndNullPartitionKey()
    {
        var settings = new UserSettings { ActiveWorkspace = "work" };

        await _service.SaveAsync(settings);

        _storeMock.Verify(store => store.Save(settings
                                            , null                          // partitionKey
                                            , UserSettingsService.SettingsId)  // id
                        , Times.Once);
    }
}

// ============================================================
// WorkspaceContext
// ============================================================

public class WorkspaceContextTests
{
    private readonly Mock<IUserSettingsService> _settingsMock = new();

    private WorkspaceContext BuildContext(string activeWorkspace = WorkspaceKeys.Personal)
    {
        _settingsMock.Setup(service => service.Get())
                     .Returns(new UserSettings { ActiveWorkspace = activeWorkspace });

        _settingsMock.Setup(service => service.SaveAsync(It.IsAny<UserSettings>()))
                     .Returns(Task.CompletedTask);

        return new WorkspaceContext(_settingsMock.Object);
    }

    [Fact]
    public void Constructor_LoadsActiveWorkspace_FromUserSettings()
    {
        var context = BuildContext("work");

        Assert.Equal("work", context.ActiveWorkspace);
    }

    [Fact]
    public void Constructor_DefaultsToPersonal_WhenSettingsReturnDefault()
    {
        var context = BuildContext(WorkspaceKeys.Personal);

        Assert.Equal(WorkspaceKeys.Personal, context.ActiveWorkspace);
    }

    [Fact]
    public void ActivePartitionKey_ReturnsNull_ForPersonalWorkspace()
    {
        var context = BuildContext(WorkspaceKeys.Personal);

        Assert.Null(context.ActivePartitionKey);
    }

    [Fact]
    public void ActivePartitionKey_ReturnsName_ForNonPersonalWorkspace()
    {
        var context = BuildContext("work");

        Assert.Equal("work", context.ActivePartitionKey);
    }

    [Fact]
    public async Task SetActiveWorkspaceAsync_UpdatesActiveWorkspace()
    {
        var context = BuildContext(WorkspaceKeys.Personal);

        await context.SetActiveWorkspaceAsync("work");

        Assert.Equal("work", context.ActiveWorkspace);
    }

    [Fact]
    public async Task SetActiveWorkspaceAsync_NormalisesNameToLowerCase()
    {
        var context = BuildContext(WorkspaceKeys.Personal);

        await context.SetActiveWorkspaceAsync("Work");

        Assert.Equal("work", context.ActiveWorkspace);
    }

    [Fact]
    public async Task SetActiveWorkspaceAsync_TrimsWhitespace()
    {
        var context = BuildContext(WorkspaceKeys.Personal);

        await context.SetActiveWorkspaceAsync("  work  ");

        Assert.Equal("work", context.ActiveWorkspace);
    }

    [Fact]
    public async Task SetActiveWorkspaceAsync_PersistsToUserSettings()
    {
        var context = BuildContext(WorkspaceKeys.Personal);

        await context.SetActiveWorkspaceAsync("work");

        _settingsMock.Verify(service => service.SaveAsync(
                                 It.Is<UserSettings>(settings => settings.ActiveWorkspace == "work"))
                           , Times.Once);
    }

    [Fact]
    public async Task SetActiveWorkspaceAsync_UpdatesActivePartitionKey()
    {
        var context = BuildContext(WorkspaceKeys.Personal);

        await context.SetActiveWorkspaceAsync("work");

        Assert.Equal("work", context.ActivePartitionKey);
    }

    [Fact]
    public async Task SetActiveWorkspaceAsync_ThrowsOnNullOrWhitespace()
    {
        var context = BuildContext();

        await Assert.ThrowsAsync<ArgumentException>(() => context.SetActiveWorkspaceAsync(""));
        await Assert.ThrowsAsync<ArgumentException>(() => context.SetActiveWorkspaceAsync("   "));
    }
}
