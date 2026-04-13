using System.Reflection;
using Moq;
using CognitivePlatform.Api.Audit;
using CognitivePlatform.Api.Execution;
using CognitivePlatform.Api.Models;
using CognitivePlatform.Api.Telemetry;

namespace CognitivePlatform.Tests;

public class ExecutionEngineTests
{
    private readonly Mock<ITelemetrySink>   _telemetryMock  = new();
    private readonly Mock<IServiceProvider> _providerMock   = new();
    private readonly Mock<IAuditLog>        _auditMock      = new();
    private readonly TelemetryContext       _telemetryCtx   = new() { SessionId = "test" };
    private readonly ExecutionEngine        _engine;

    public ExecutionEngineTests()
    {
        _engine = new ExecutionEngine(_telemetryMock.Object
                                    , _providerMock.Object
                                    , _telemetryCtx
                                    , _auditMock.Object);
    }

    // ================================================================
    // AUDIT LOG — success path
    // ================================================================

    [Fact]
    public void Execute_AppendsAuditEvent_WithSuccessOutcome_OnSuccess()
    {
        var action = MakeAction(nameof(FakeActions.ReturnOk));

        _engine.Execute(action, new Dictionary<string, string>(), "session-1");

        _auditMock.Verify(log => log.Append(It.Is<AuditEvent>(auditEvent => auditEvent.Outcome    == AuditOutcome.Success
                                                                         && auditEvent.ActionName == "ReturnOk"))
                        , Times.Once);
    }

    [Fact]
    public void Execute_AppendsAuditEvent_WithFailureOutcome_OnException()
    {
        var action = MakeAction(nameof(FakeActions.ThrowBoom));

        _engine.Execute(action, new Dictionary<string, string>(), "session-1");

        _auditMock.Verify(log => log.Append(It.Is<AuditEvent>(auditEvent => auditEvent.Outcome      == AuditOutcome.Failure
                                                                         && auditEvent.ErrorMessage != null
                                                                         && auditEvent.ActionName   == "ThrowBoom"))
                        , Times.Once);
    }

    [Fact]
    public void Execute_IncludesSessionId_InAuditEventMeta()
    {
        var action = MakeAction(nameof(FakeActions.ReturnOk));

        _engine.Execute(action, new Dictionary<string, string>(), "session-abc");

        _auditMock.Verify(log => log.Append(It.Is<AuditEvent>(auditEvent => auditEvent.Meta["sessionId"] == "session-abc"))
                        , Times.Once);
    }

    // ================================================================
    // ASYNC UNWRAPPING
    // ================================================================

    [Fact]
    public void Execute_UnwrapsTaskT_AndReturnsInnerValue()
    {
        var action = MakeAction(nameof(FakeActions.ReturnAsyncString));

        var result = _engine.Execute(action, new Dictionary<string, string>(), "s");

        Assert.Equal("async-result", result);
    }

    [Fact]
    public void Execute_UnwrapsNonGenericTask_AndReturnsNoReturnValueMessage()
    {
        var action = MakeAction(nameof(FakeActions.ReturnCompletedTask));

        var result = _engine.Execute(action, new Dictionary<string, string>(), "s");

        Assert.Contains("no return value", result);
    }

    // ================================================================
    // HELPERS
    // ================================================================

    private static ActionMetadata MakeAction(string methodName)
    {
        var method = typeof(FakeActions).GetMethod(methodName
                                                  , BindingFlags.Public | BindingFlags.Static)!;
        return new ActionMetadata
               {
                       Name       = methodName
                     , MethodInfo = method
                     , Parameters = new List<ParameterMetadata>()
                     , Category   = "Test"
               };
    }

    /// <summary>
    /// Minimal static methods that ExecutionEngine can invoke via reflection.
    /// Static so CreateTargetInstance returns null (no service provider call needed).
    /// </summary>
    private static class FakeActions
    {
        public static string ReturnOk()                    => "OK";
        public static string ThrowBoom()                   => throw new InvalidOperationException("boom");
        public static async Task<string> ReturnAsyncString() { await Task.Yield(); return "async-result"; }
        public static Task ReturnCompletedTask()           => Task.CompletedTask;
    }
}
