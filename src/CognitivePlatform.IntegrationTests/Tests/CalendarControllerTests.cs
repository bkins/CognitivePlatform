using System.Net;
using CognitivePlatform.IntegrationTests.Infrastructure;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace CognitivePlatform.IntegrationTests.Tests;

[Trait("Category", "Integration")]
public sealed class CalendarControllerTests : IDisposable
{
    private readonly ApiFixture _fixture;

    public CalendarControllerTests(ITestOutputHelper output)
    {
        _fixture = new ApiFixture(output);
    }

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task Status_ReturnsConnectionStatus()
    {
        _fixture.Log("Act — GET /auth/google/status");
        var response = await _fixture.Client.GetAsync("/auth/google/status");

        _fixture.LogAssertion("returns 200 OK with connected property");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("connected");
    }

    [Fact]
    public async Task EventsByDate_ReturnsBadRequest_WhenDateInvalid()
    {
        _fixture.Log("Act — GET /auth/google/eventsbydate?date=not-a-date");
        var response = await _fixture.Client.GetAsync("/auth/google/eventsbydate?date=not-a-date");

        _fixture.LogAssertion("returns 400 BadRequest with clear error");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("couldn't parse");
    }
}
