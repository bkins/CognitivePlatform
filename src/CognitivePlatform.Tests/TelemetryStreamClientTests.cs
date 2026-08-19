using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CP.Client.Core.Telemetry;
using Xunit;

namespace CognitivePlatform.Tests;

public sealed class TelemetryStreamClientTests
{
    private class FakeSseHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _sseContent;

        public FakeSseHttpMessageHandler(string sseContent)
        {
            _sseContent = sseContent;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
                           {
                               Content = new StringContent(_sseContent, Encoding.UTF8, "text/event-stream")
                           };

            return Task.FromResult(response);
        }
    }

    [Fact]
    public async Task SubscribeAsync_ParsesSseDataLines_YieldsTelemetryEventDtos()
    {
        var ssePayload = """
                         id: 05244b7d-6fca-443b-a25e-3c224e7561be
                         event: Conversation.End
                         data: {"eventName":"Conversation.End","sessionId":"test-session-1","durationMs":350.5,"success":true}

                         id: 11223344-5566-7788-99aa-bbccddeeff00
                         event: Action.CreateTask
                         data: {"eventName":"Action.CreateTask","sessionId":"test-session-2","durationMs":80.0,"success":true}

                         """;

        var handler    = new FakeSseHttpMessageHandler(ssePayload);
        var httpClient = new HttpClient(handler);
        var client     = new TelemetryStreamClient(httpClient);

        var events = new List<TelemetryEventDto>();

        await foreach (var evt in client.SubscribeAsync("http://localhost:5000", CancellationToken.None))
        {
            events.Add(evt);
        }

        Assert.Equal(2, events.Count);
        Assert.Equal("Conversation.End", events[0].EventName);
        Assert.Equal("test-session-1", events[0].SessionId);
        Assert.Equal(350.5, events[0].DurationMs);
        Assert.True(events[0].Success);

        Assert.Equal("Action.CreateTask", events[1].EventName);
        Assert.Equal("test-session-2", events[1].SessionId);
        Assert.Equal(80.0, events[1].DurationMs);
        Assert.True(events[1].Success);
    }
}
