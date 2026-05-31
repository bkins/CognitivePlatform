using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CognitivePlatform.IntegrationTests.Infrastructure;
using Xunit.Abstractions;

namespace CognitivePlatform.IntegrationTests.Tests;

[Trait("Category", "Integration")]
public sealed class PersonalityControllerTests : IDisposable
{
    private readonly ApiFixture _fixture;

    public PersonalityControllerTests(ITestOutputHelper output)
    {
        _fixture = new ApiFixture(output);
    }

    public void Dispose() => _fixture.Dispose();

    // ----------------------------------------------------------------
    // GET /api/personality
    // ----------------------------------------------------------------

    [Fact]
    public async Task GetAll_Returns200_WithArrayBody()
    {
        var response = await _fixture.Client.GetAsync("/api/personality");

        _fixture.LogAssertion("status code is 200 OK");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await _fixture.ReadJsonAsync<JsonElement>(response);

        _fixture.LogAssertion("body is a JSON array");
        body.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task GetAll_EachItem_HasNameField()
    {
        var response = await _fixture.Client.GetAsync("/api/personality");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var personalities = await _fixture.ReadJsonAsync<JsonElement>(response);

        _fixture.LogAssertion("every personality has a name field");
        foreach (var personality in personalities.EnumerateArray())
        {
            personality.TryGetProperty("name", out _).Should().BeTrue(
                "personality definition must have a name");
        }
    }

    // ----------------------------------------------------------------
    // GET /api/personality/active
    // ----------------------------------------------------------------

    [Fact]
    public async Task GetActive_Returns200Or404()
    {
        var response = await _fixture.Client.GetAsync("/api/personality/active");

        // 200 if an active personality is set; 404 if none is configured.
        _fixture.LogAssertion("status code is 200 or 404");
        ((int)response.StatusCode).Should().BeOneOf(200, 404);
    }

    [Fact]
    public async Task GetActive_WhenExists_HasNameField()
    {
        var response = await _fixture.Client.GetAsync("/api/personality/active");

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            _fixture.Log("Skip — no active personality configured");
            return;
        }

        _fixture.LogAssertion("status code is 200 OK");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var active = await _fixture.ReadJsonAsync<JsonElement>(response);

        _fixture.LogAssertion("active personality has a name field");
        active.TryGetProperty("name", out _).Should().BeTrue(
            "active personality must have a name");
    }

    // ----------------------------------------------------------------
    // PUT /api/personality/{id}/activate
    // ----------------------------------------------------------------

    [Fact]
    public async Task Activate_Returns404_ForUnknownId()
    {
        var unknownId = $"unknown-personality-{Guid.NewGuid():N}";

        var response = await _fixture.Client.PutAsync(
            $"/api/personality/{unknownId}/activate", content: null);

        _fixture.LogAssertion("status code is 404 Not Found");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ----------------------------------------------------------------
    // Round-trip: activate an existing personality
    // ----------------------------------------------------------------

    [Fact]
    public async Task Activate_AndGetActive_RoundTrip_WhenPersonalitiesExist()
    {
        _fixture.Log("Arrange — fetch personality list");
        var listResponse = await _fixture.Client.GetAsync("/api/personality");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var personalities = await _fixture.ReadJsonAsync<JsonElement>(listResponse);

        if (personalities.GetArrayLength() == 0)
        {
            _fixture.Log("Skip — no personalities seeded in live DB");
            return;
        }

        var firstPersonality = personalities.EnumerateArray().First();

        if (!firstPersonality.TryGetProperty("id", out var idProp))
        {
            _fixture.Log("Skip — personality list items have no 'id' property");
            return;
        }

        var personalityId = idProp.GetString()!;

        _fixture.Log($"Act — PUT /api/personality/{personalityId}/activate");
        var activateResponse = await _fixture.Client.PutAsync(
            $"/api/personality/{personalityId}/activate", content: null);

        _fixture.LogAssertion("activate returns 200 OK");
        activateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        _fixture.Log("Assert — GET /api/personality/active returns the activated personality");
        var activeResponse = await _fixture.Client.GetAsync("/api/personality/active");

        _fixture.LogAssertion("active endpoint returns 200 OK");
        activeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var active = await _fixture.ReadJsonAsync<JsonElement>(activeResponse);

        _fixture.LogAssertion($"active personality id equals '{personalityId}'");
        active.GetProperty("id").GetString().Should().Be(personalityId,
            "the activated personality should be the current active one");
    }
}
