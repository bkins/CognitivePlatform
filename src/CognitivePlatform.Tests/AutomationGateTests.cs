using Moq;
using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Insights;
using CognitivePlatform.Api.Insights.Models;
using CognitivePlatform.Api.Workspace;
using Microsoft.Extensions.Logging;

namespace CognitivePlatform.Tests;

public class AutomationGateTests
{
    private readonly Mock<IUserSettingsService> _settingsServiceMock = new();
    private readonly Mock<IObjectStore>          _storeMock           = new();
    private readonly Mock<ILogger<AutomationGate>> _loggerMock         = new();

    private AutomationGate BuildGate(IEnumerable<string> allowed)
    {
        var settings = new UserSettings
        {
            AllowedAutomationActions = new HashSet<string>(allowed, StringComparer.OrdinalIgnoreCase)
        };

        _settingsServiceMock.Setup(s => s.Get()).Returns(settings);

        _storeMock.Setup(s => s.Save(It.IsAny<AutomationAudit>(), It.IsAny<string?>(), It.IsAny<string?>()))
                  .ReturnsAsync("some-id");

        return new AutomationGate(_settingsServiceMock.Object, _storeMock.Object, _loggerMock.Object);
    }

    private static Dictionary<string, string> EmptyParams() => new();

    // ====================================================================
    // Whitelist — allow
    // ====================================================================

    [Fact]
    public void CanAutoExecute_ReturnsTrue_WhenActionIsWhitelisted()
    {
        var gate = BuildGate(new[] { "AddJournalEntry" });

        var result = gate.CanAutoExecute("AddJournalEntry", EmptyParams());

        Assert.True(result);
    }

    // ====================================================================
    // Whitelist — deny
    // ====================================================================

    [Fact]
    public void CanAutoExecute_ReturnsFalse_WhenActionIsNotWhitelisted()
    {
        var gate = BuildGate(Array.Empty<string>());

        var result = gate.CanAutoExecute("DeleteTask", EmptyParams());

        Assert.False(result);
    }

    // ====================================================================
    // Auditing — saves audit to Object Store and logs it
    // ====================================================================

    [Fact]
    public void CanAutoExecute_SavesAuditAndLogsEveryCall_RegardlessOfResult()
    {
        var gate = BuildGate(new[] { "AddJournalEntry" });

        gate.CanAutoExecute("AddJournalEntry", EmptyParams()); // whitelisted → true
        gate.CanAutoExecute("DeleteTask",      EmptyParams()); // not listed  → false

        // Verify that it saved to the Object Store exactly twice.
        _storeMock.Verify(
            s => s.Save(It.IsAny<AutomationAudit>(), It.Is<string>(p => p == "automation-audit"), It.IsAny<string?>())
          , Times.Exactly(2));

        // Verify that it logged to ILogger exactly twice.
        _loggerMock.Verify(
            logger => logger.Log(
                It.Is<LogLevel>(logLevel => logLevel == LogLevel.Information)
              , It.IsAny<EventId>()
              , It.IsAny<It.IsAnyType>()
              , It.IsAny<Exception?>()
              , It.IsAny<Func<It.IsAnyType, Exception?, string>>())
          , Times.Exactly(2));
    }
}
