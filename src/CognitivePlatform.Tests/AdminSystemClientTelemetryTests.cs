using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CognitivePlatform.Admin.CpAdminClients;
using Xunit;

namespace CognitivePlatform.Tests;

public sealed class AdminSystemClientTelemetryTests
{
    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request));
        }
    }

    [Fact]
    public async Task GetTelemetryMetricsAsync_ReturnsDeserializedMetrics()
    {
        var jsonPayload = """
            [
              {
                "operationName": "Converse.Ended",
                "count": 42,
                "averageDurationMs": 150.5,
                "minDurationMs": 80.0,
                "maxDurationMs": 300.0,
                "successRate": 0.98,
                "lastActivity": "2026-08-19T12:00:00Z"
              }
            ]
            """;

        var handler = new MockHttpMessageHandler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/api/system/telemetry", request.RequestUri?.PathAndQuery);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
            };
        });

        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost/")
        };

        var client = new AdminSystemClient(httpClient);

        var metrics = await client.GetTelemetryMetricsAsync();

        Assert.NotNull(metrics);
        Assert.Single(metrics);
        Assert.Equal("Converse.Ended", metrics[0].OperationName);
        Assert.Equal(42,               metrics[0].Count);
        Assert.Equal(150.5,            metrics[0].AverageDurationMs);
        Assert.Equal(80.0,             metrics[0].MinDurationMs);
        Assert.Equal(300.0,            metrics[0].MaxDurationMs);
        Assert.Equal(0.98,             metrics[0].SuccessRate);
    }
}
