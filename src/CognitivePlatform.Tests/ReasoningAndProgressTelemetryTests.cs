using System;
using CognitivePlatform.Api.Contracts;
using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Telemetry;
using CognitivePlatform.Api.Telemetry.Events;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace CognitivePlatform.Tests;

public sealed class ReasoningAndProgressTelemetryTests
{
    [Fact]
    public void OrchestratorProgressEvent_FormatsStageAndMessage()
    {
        var progressEvent = new OrchestratorProgressEvent
                            {
                                    Stage        = "Routing"
                                  , StageMessage = "Evaluating fast-path rules and domain classification..."
                                  , SessionId    = "session-123"
                            };

        var output = progressEvent.ToString();

        Assert.Equal("Orchestrator.Progress", progressEvent.EventName);
        Assert.Equal("Routing",               progressEvent.Stage);
        Assert.Contains("[Routing]", output);
        Assert.Contains("Evaluating fast-path rules", output);
    }

    [Fact]
    public void ReasoningDeltaEvent_TracksTokenDeltaAndTotal()
    {
        var deltaEvent = new ReasoningDeltaEvent
                         {
                                 ReasoningDelta     = "Analyzing user tasks..."
                               , RunningTotalTokens = 15
                               , SessionId          = "session-123"
                         };

        var output = deltaEvent.ToString();

        Assert.Equal("Reasoning.Delta", deltaEvent.EventName);
        Assert.Equal("Analyzing user tasks...", deltaEvent.ReasoningDelta);
        Assert.Equal(15,                        deltaEvent.RunningTotalTokens);
        Assert.Contains("Delta: Analyzing user tasks...", output);
        Assert.Contains("Tokens: 15", output);
    }

    [Fact]
    public void ConverseResponse_ReasoningContent_IsNeverNullByDefault()
    {
        var response = new ConverseResponse();

        Assert.NotNull(response.ReasoningContent);
        Assert.NotEmpty(response.ReasoningContent);
        Assert.Contains("Standard Completion", response.ReasoningContent);
    }

    [Fact]
    public void PersistentConversationTelemetrySink_PublishesProgressAndReasoningToStream()
    {
        var streamMock = new Mock<ITelemetryStreamService>();
        var storeMock  = new Mock<IObjectStore>();
        var context    = new TelemetryContext { SessionId = "test-session" };
        var console    = new ConsoleTelemetrySink(NullLogger<ConsoleTelemetrySink>.Instance, context);

        var sink = new PersistentConversationTelemetrySink(
            console
          , storeMock.Object
          , streamMock.Object
          , NullLogger<PersistentConversationTelemetrySink>.Instance);

        var progressEvent = new OrchestratorProgressEvent
                            {
                                    Stage        = "ExecutingAction"
                                  , StageMessage = "Executing 'Tasks.CreateTask'"
                                  , SessionId    = "test-session"
                            };

        var deltaEvent = new ReasoningDeltaEvent
                         {
                                 ReasoningDelta     = "Found 2 matching items"
                               , RunningTotalTokens = 8
                               , SessionId          = "test-session"
                         };

        sink.Track(progressEvent);
        sink.Track(deltaEvent);

        streamMock.Verify(s => s.Publish(It.Is<TelemetryEvent>(e => e.EventName == "Orchestrator.Progress")), Times.Once);
        streamMock.Verify(s => s.Publish(It.Is<TelemetryEvent>(e => e.EventName == "Reasoning.Delta")), Times.Once);
    }
}
