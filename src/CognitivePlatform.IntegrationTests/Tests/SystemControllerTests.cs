using System.Net;
using System.Text.Json;
using CognitivePlatform.IntegrationTests.Infrastructure;
using Xunit.Abstractions;

namespace CognitivePlatform.IntegrationTests.Tests;

[Trait("Category", "Integration")]
public sealed class SystemControllerTests : IDisposable
{
    private readonly ApiFixture _fixture;

    public SystemControllerTests(ITestOutputHelper output)
    {
        _fixture = new ApiFixture(output);
    }

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task Ping_Returns200_WithPongBody()
    {
        var response = await _fixture.Client.GetAsync("/api/system/ping");

        _fixture.LogAssertion("status code is 200 OK");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await _fixture.ReadJsonAsync<JsonElement>(response);

        _fixture.LogAssertion("pong field equals 'Pong'");
        body.GetProperty("pong").GetString().Should().Be("Pong");
    }

    [Fact]
    public async Task GetEnvironment_Returns200_WithMessageAndData()
    {
        var response = await _fixture.Client.GetAsync("/api/system/environment");

        _fixture.LogAssertion("status code is 200 OK");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await _fixture.ReadJsonAsync<JsonElement>(response);

        _fixture.LogAssertion("body contains 'message' property");
        body.TryGetProperty("message", out _).Should().BeTrue(
            "response should contain a 'message' property");

        _fixture.LogAssertion("body contains 'data' property");
        body.TryGetProperty("data", out _).Should().BeTrue(
            "response should contain a 'data' property");
    }

    [Fact]
    public async Task GetUsage_Returns200_WithExpectedShape()
    {
        var response = await _fixture.Client.GetAsync("/api/system/usage");

        _fixture.LogAssertion("status code is 200 OK");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await _fixture.ReadJsonAsync<JsonElement>(response);

        _fixture.LogAssertion("body contains hasData, requests, tokens, sessionTotals");
        body.TryGetProperty("hasData",       out _).Should().BeTrue("hasData must be present");
        body.TryGetProperty("requests",      out _).Should().BeTrue("requests must be present");
        body.TryGetProperty("tokens",        out _).Should().BeTrue("tokens must be present");
        body.TryGetProperty("sessionTotals", out _).Should().BeTrue("sessionTotals must be present");
    }

    [Fact]
    public async Task GetUsage_SessionTotals_ContainsTokenCounts()
    {
        var response = await _fixture.Client.GetAsync("/api/system/usage");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body          = await _fixture.ReadJsonAsync<JsonElement>(response);
        var sessionTotals = body.GetProperty("sessionTotals");

        _fixture.LogAssertion("sessionTotals has promptTokens, completionTokens, totalTokens");
        sessionTotals.TryGetProperty("promptTokens",     out _).Should().BeTrue();
        sessionTotals.TryGetProperty("completionTokens", out _).Should().BeTrue();
        sessionTotals.TryGetProperty("totalTokens",      out _).Should().BeTrue();
    }

    [Fact]
    public async Task GetTelemetry_Returns200_WithArrayBody()
    {
        var response = await _fixture.Client.GetAsync("/api/system/telemetry");

        _fixture.LogAssertion("status code is 200 OK");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await _fixture.ReadJsonAsync<JsonElement>(response);

        _fixture.LogAssertion("body is a JSON array");
        body.ValueKind.Should().Be(JsonValueKind.Array,
            "telemetry endpoint returns an array of metrics");
    }
}
