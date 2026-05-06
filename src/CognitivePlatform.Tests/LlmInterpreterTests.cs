using CognitivePlatform.Api.Avails;
using CognitivePlatform.Api.Avails.Models;
using CognitivePlatform.Api.Conversation;
using CognitivePlatform.Api.Interpreter;
using CognitivePlatform.Api.Models;
using CognitivePlatform.Api.Registry;
using CognitivePlatform.Api.SystemPromptLogging;
using CognitivePlatform.Api.Telemetry;
using Moq;

namespace CognitivePlatform.Tests;

// BUG-34: LlmInterpreter null-safety regression tests.
// The extraReason line accessed modelInfo.FailureReason without a null check,
// crashing with NullReferenceException when the requested model was absent
// from the catalog. These tests verify both the null-model and the unusable-model
// paths return a meaningful result rather than throwing.
public class LlmInterpreterTests
{
    private readonly Mock<IActionRegistry> _registryMock  = new();
    private readonly Mock<ITelemetrySink>  _telemetryMock = new();
    private readonly Mock<ILlmRouter>      _routerMock    = new();
    private readonly Mock<IPromptLogger>   _promptLogMock = new();

    private LlmInterpreter BuildInterpreter( LlmModelCatalog   catalog
                                           , LlmClientSettings? settings = null )
    {
        _registryMock.SetupGet(registry => registry.Actions)
                     .Returns(Array.Empty<ActionMetadata>());

        return new LlmInterpreter( _registryMock.Object
                                 , _telemetryMock.Object
                                 , _routerMock.Object
                                 , catalog
                                 , settings ?? new LlmClientSettings()
                                 , _promptLogMock.Object );
    }

    [Fact]
    public async Task InterpretWithContext_WhenModelNotInCatalog_ReturnsNoModelResult_WithoutThrowingNullRef()
    {
        // Arrange: empty catalog so FirstOrDefault returns null → modelInfo is null
        var emptyCatalog = new LlmModelCatalog();
        var interpreter  = BuildInterpreter(emptyCatalog);
        var context      = new ConversationContext("test-session");

        // Act: previously threw NullReferenceException at the extraReason line
        var result = await interpreter.InterpretWithContext("what time is it", context);

        // Assert: returns a structured failure rather than an unhandled exception
        Assert.Equal(InterpreterFailureType.NoMatchingAction, result.FailureType);
        Assert.NotNull(result.Reason);
    }

    [Fact]
    public async Task InterpretWithContext_WhenModelIsNotUsable_WithHttp429Reason_IncludesRateLimitDetail()
    {
        // Arrange: model is in catalog but flagged unusable with a rate-limit reason
        const string failureReason = "HTTP 429: too many requests — daily quota exhausted";

        var catalog = new LlmModelCatalog();
        catalog.Add(new LlmModelInfo( Name             : "gemini-2.5-flash"
                                    , IsUsable          : false
                                    , FailureReason     : failureReason
                                    , SupportsChat      : true
                                    , SupportsStreaming  : true ));

        var settings = new LlmClientSettings
                       {
                               Provider     = LlmProvider.Gemini
                             , DefaultModel = "gemini-2.5-flash"
                       };

        var interpreter = BuildInterpreter(catalog, settings);
        var context     = new ConversationContext("test-session");
        context.Metadata["model"] = "gemini-2.5-flash";

        // Act
        var result = await interpreter.InterpretWithContext("what time is it", context);

        // Assert: the HTTP 429 detail is surfaced in the reason so the caller can
        // distinguish a rate-limit failure from a missing-model failure
        Assert.Equal(InterpreterFailureType.NoMatchingAction, result.FailureType);
        Assert.Contains("HTTP 429:", result.Reason);
    }

}
