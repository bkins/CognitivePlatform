using System.Reflection;
using Moq;
using CognitivePlatform.Api.Audit;
using CognitivePlatform.Api.Execution;
using CognitivePlatform.Api.Models;
using CognitivePlatform.Api.Registry;
using CognitivePlatform.Api.Registry.Domains;
using CognitivePlatform.Api.Telemetry;

namespace CognitivePlatform.Tests;

public class ActionDefinitionBuilderTests
{
    // ================================================================
    // Builder — valid construction
    // ================================================================

    [Fact]
    public void Build_ProducesValidMetadata_WithAllFieldsSet()
    {
        var handler = new Mock<IActionHandler>();
        var domain  = new SystemDomain();

        var metadata = new ActionDefinitionBuilder()
            .Named("TestAction")
            .WithDescription("A test action")
            .InDomain(domain)
            .WithExamples("Example one", "Example two")
            .WithParameter("param1", typeof(string), "A string parameter")
            .AllowsClarification()
            .AsReplayable()
            .HandledBy(handler.Object)
            .Build();

        Assert.Equal("TestAction",     metadata.Name);
        Assert.Equal("A test action",  metadata.Description);
        Assert.Equal("System",         metadata.Category);
        Assert.Same(domain,            metadata.Domain);
        Assert.Equal(2,                metadata.Examples?.Length);
        Assert.Single(metadata.Parameters);
        Assert.True(metadata.AllowsClarification);
        Assert.True(metadata.IsReplayable);
        Assert.Same(handler.Object,    metadata.Handler);
        Assert.Null(metadata.MethodInfo);
    }

    // ================================================================
    // Builder — invalid state guards
    // ================================================================

    [Fact]
    public void Build_ThrowsInvalidOperationException_WhenNameNotSet()
    {
        var builder = new ActionDefinitionBuilder()
            .WithDescription("No name provided");

        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }

    [Fact]
    public void Build_ThrowsInvalidOperationException_WhenNameIsWhitespace()
    {
        var builder = new ActionDefinitionBuilder()
            .Named("   ");

        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }

    // ================================================================
    // ExecutionEngine — handler routing
    // ================================================================

    [Fact]
    public async Task ExecuteAsync_CallsHandler_AndReturnsItsMessage_WhenHandlerIsNotNull()
    {
        var telemetryMock = new Mock<ITelemetrySink>();
        var providerMock  = new Mock<IServiceProvider>();
        var auditMock     = new Mock<IAuditLog>();
        var handlerMock   = new Mock<IActionHandler>();
        var telemetryCtx  = new TelemetryContext { SessionId = "test-handler" };

        auditMock.Setup(log => log.AppendAsync(It.IsAny<AuditEvent>()))
                 .Returns(Task.CompletedTask);

        handlerMock.Setup(handler => handler.ExecuteAsync(It.IsAny<ActionExecutionContext>()))
                   .ReturnsAsync(new ActionResult { Success = true, Message = "handler-response" });

        var engine = new ExecutionEngine(telemetryMock.Object
                                       , providerMock.Object
                                       , telemetryCtx
                                       , auditMock.Object);

        var metadata = new ActionMetadata
                       {
                               Name       = "HandlerAction"
                             , Handler    = handlerMock.Object
                             , Parameters = new List<ParameterMetadata>()
                             , Category   = "Test"
                       };

        var result = await engine.ExecuteAsync(metadata, new Dictionary<string, string>(), "session-1");

        handlerMock.Verify(handler => handler.ExecuteAsync(It.IsAny<ActionExecutionContext>()), Times.Once);
        Assert.Equal("handler-response", result);
    }

    [Fact]
    public async Task ExecuteAsync_UsesReflection_WhenHandlerIsNull()
    {
        var telemetryMock = new Mock<ITelemetrySink>();
        var providerMock  = new Mock<IServiceProvider>();
        var auditMock     = new Mock<IAuditLog>();
        var telemetryCtx  = new TelemetryContext { SessionId = "test-reflection" };

        auditMock.Setup(log => log.AppendAsync(It.IsAny<AuditEvent>()))
                 .Returns(Task.CompletedTask);

        var engine = new ExecutionEngine(telemetryMock.Object
                                       , providerMock.Object
                                       , telemetryCtx
                                       , auditMock.Object);

        var method = typeof(ReflectionTarget).GetMethod(nameof(ReflectionTarget.Greet)
                                                       , BindingFlags.Public | BindingFlags.Static)!;

        var metadata = new ActionMetadata
                       {
                               Name       = "Greet"
                             , Handler    = null
                             , MethodInfo = method
                             , Parameters = new List<ParameterMetadata>()
                             , Category   = "Test"
                       };

        var result = await engine.ExecuteAsync(metadata, new Dictionary<string, string>(), "session-2");

        Assert.Equal("hello from reflection", result);
    }

    // ================================================================
    // ActionRegistry — duplicate registration guard
    // ================================================================

    [Fact]
    public void Register_ThrowsInvalidOperationException_WhenNameAlreadyRegistered()
    {
        var registry = new ActionRegistry();

        var first = new ActionDefinitionBuilder()
            .Named("Enh22_ProofAction")
            .WithDescription("First registration")
            .HandledBy(Mock.Of<IActionHandler>())
            .Build();

        registry.Register(first);

        var duplicate = new ActionDefinitionBuilder()
            .Named("Enh22_ProofAction")
            .WithDescription("Duplicate — should throw")
            .HandledBy(Mock.Of<IActionHandler>())
            .Build();

        Assert.Throws<InvalidOperationException>(() => registry.Register(duplicate));
    }

    // ================================================================
    // Internal helpers
    // ================================================================

    private static class ReflectionTarget
    {
        public static string Greet() => "hello from reflection";
    }
}
