using System.Net;
using CognitivePlatform.Api.Insights.Models;
using CognitivePlatform.IntegrationTests.Infrastructure;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace CognitivePlatform.IntegrationTests.Tests;

[Trait("Category", "Integration")]
public sealed class InsightsControllerTests : IDisposable
{
    private readonly ApiFixture _fixture;

    public InsightsControllerTests(ITestOutputHelper output)
    {
        _fixture = new ApiFixture(output);
    }

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task Evaluate_ReturnsEvaluatedInsightsList()
    {
        _fixture.Log("Act — POST /api/insights/evaluate");
        var response = await _fixture.Client.PostAsync("/api/insights/evaluate?sessionId=test-session-123", null);

        _fixture.LogAssertion("returns 200 OK with insights array");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var insights = await _fixture.ReadJsonAsync<List<Insight>>(response);
        insights.Should().NotBeNull();
    }
}
