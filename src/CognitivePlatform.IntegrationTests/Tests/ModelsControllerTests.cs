using System.Net;
using CognitivePlatform.Api.Avails.Models;
using CognitivePlatform.IntegrationTests.Infrastructure;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace CognitivePlatform.IntegrationTests.Tests;

[Trait("Category", "Integration")]
public sealed class ModelsControllerTests : IDisposable
{
    private readonly ApiFixture _fixture;

    public ModelsControllerTests(ITestOutputHelper output)
    {
        _fixture = new ApiFixture(output);
    }

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task Get_ReturnsAvailableModels()
    {
        _fixture.Log("Act — GET /api/models");
        var response = await _fixture.Client.GetAsync("/api/models");

        _fixture.LogAssertion("returns 200 OK");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var models = await _fixture.ReadJsonAsync<List<LlmModelInfo>>(response);
        models.Should().NotBeNull();
    }
}
