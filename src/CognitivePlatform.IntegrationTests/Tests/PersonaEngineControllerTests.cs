using System.Net;
using System.Net.Http.Json;
using CognitivePlatform.Api.Controllers;
using CognitivePlatform.Api.Domains.PersonaEngine.Models;
using CognitivePlatform.IntegrationTests.Infrastructure;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace CognitivePlatform.IntegrationTests.Tests;

[Trait("Category", "Integration")]
public sealed class PersonaEngineControllerTests : IDisposable
{
    private readonly ApiFixture _fixture;

    public PersonaEngineControllerTests(ITestOutputHelper output)
    {
        _fixture = new ApiFixture(output);
    }

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task Resolve_ReturnsContextResult_ForValidMessage()
    {
        _fixture.Log("Act — POST /api/persona-engine/resolve");
        var request = new ResolvePersonaRequest("I need help organizing my upcoming week");
        var response = await _fixture.Client.PostAsJsonAsync("/api/persona-engine/resolve", request);

        _fixture.LogAssertion("returns 200 OK with persona context result");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await _fixture.ReadJsonAsync<PersonaContextResult>(response);
        result.Should().NotBeNull();
        result!.IntentAnalysisResult.Should().NotBeNull();
    }

    [Fact]
    public async Task Resolve_ReturnsBadRequest_WhenMessageEmpty()
    {
        _fixture.Log("Act — POST /api/persona-engine/resolve with empty message");
        var request = new ResolvePersonaRequest(string.Empty);
        var response = await _fixture.Client.PostAsJsonAsync("/api/persona-engine/resolve", request);

        _fixture.LogAssertion("returns 400 BadRequest");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
