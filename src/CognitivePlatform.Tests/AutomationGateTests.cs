using Moq;
using CognitivePlatform.Api.Insights;
using Microsoft.Extensions.Logging;

namespace CognitivePlatform.Tests;

public class AutomationGateTests
{
    private readonly Mock<ILogger<AutomationGate>> _loggerMock = new();

    private AutomationGate BuildGate(IEnumerable<string> allowed)
    {
        var allowedSet = new HashSet<string>(allowed, StringComparer.OrdinalIgnoreCase);
        return new AutomationGate(allowedSet, _loggerMock.Object);
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
    // Audit logging — every call is logged regardless of outcome
    // ====================================================================

    [Fact]
    public void CanAutoExecute_LogsEveryCall_RegardlessOfResult()
    {
        var gate = BuildGate(new[] { "AddJournalEntry" });

        gate.CanAutoExecute("AddJournalEntry", EmptyParams()); // whitelisted → true
        gate.CanAutoExecute("DeleteTask",      EmptyParams()); // not listed  → false

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
