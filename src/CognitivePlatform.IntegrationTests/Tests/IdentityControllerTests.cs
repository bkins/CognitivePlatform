using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CognitivePlatform.IntegrationTests.Infrastructure;
using Xunit.Abstractions;

namespace CognitivePlatform.IntegrationTests.Tests;

[Trait("Category", "Integration")]
public sealed class IdentityControllerTests : IDisposable
{
    private readonly ApiFixture _fixture;

    public IdentityControllerTests(ITestOutputHelper output)
    {
        _fixture = new ApiFixture(output);
    }

    public void Dispose() => _fixture.Dispose();

    // ----------------------------------------------------------------
    // GET /api/identity/profile
    // ----------------------------------------------------------------

    [Fact]
    public async Task GetProfile_Returns200()
    {
        var response = await _fixture.Client.GetAsync("/api/identity/profile");

        _fixture.LogAssertion("status code is 200 OK");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetProfile_ReturnsValidJson()
    {
        var response = await _fixture.Client.GetAsync("/api/identity/profile");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Profile is a well-formed JSON object (may have null/empty fields on a fresh DB)
        var body = await _fixture.ReadJsonAsync<JsonElement>(response);

        _fixture.LogAssertion("body is a JSON object");
        body.ValueKind.Should().Be(JsonValueKind.Object);
    }

    // ----------------------------------------------------------------
    // GET /api/identity/assertions
    // ----------------------------------------------------------------

    [Fact]
    public async Task GetAssertions_Returns200_WithArrayBody()
    {
        var response = await _fixture.Client.GetAsync("/api/identity/assertions");

        _fixture.LogAssertion("status code is 200 OK");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await _fixture.ReadJsonAsync<JsonElement>(response);

        _fixture.LogAssertion("body is a JSON array");
        body.ValueKind.Should().Be(JsonValueKind.Array);
    }

    // ----------------------------------------------------------------
    // POST /api/identity/assertions — create and confirm round-trip
    // ----------------------------------------------------------------

    [Fact]
    public async Task AddAssertion_Returns200_WithAssertionBody()
    {
        var assertion = new
        {
            Id        = Guid.NewGuid().ToString()
          , Statement = $"Integration test assertion {Guid.NewGuid():N}"
          , Source    = "integration-test"
          , IsActive  = true
        };

        _fixture.Log($"Act — POST /api/identity/assertions (statement: '{assertion.Statement}')");
        var response = await _fixture.Client.PostAsJsonAsync(
            "/api/identity/assertions", assertion);

        _fixture.LogAssertion("status code is 200 OK");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await _fixture.ReadJsonAsync<JsonElement>(response);

        _fixture.LogAssertion($"returned statement matches '{assertion.Statement}'");
        body.GetProperty("statement").GetString().Should().Be(assertion.Statement);
    }

    [Fact]
    public async Task ConfirmAssertion_Returns200_ForExistingAssertion()
    {
        // Create an assertion to confirm
        var assertionId = Guid.NewGuid().ToString();

        var createPayload = new
        {
            Id        = assertionId
          , Statement = $"Assertion to confirm {Guid.NewGuid():N}"
          , Source    = "integration-test"
          , IsActive  = true
        };

        _fixture.Log("Arrange — POST /api/identity/assertions");
        var createResponse = await _fixture.Client.PostAsJsonAsync(
            "/api/identity/assertions", createPayload);

        _fixture.LogAssertion("create returns 200 OK");
        createResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        _fixture.Log($"Act — PUT /api/identity/assertions/{assertionId}/confirm");
        var confirmResponse = await _fixture.Client.PutAsync(
            $"/api/identity/assertions/{assertionId}/confirm", content: null);

        _fixture.LogAssertion("confirm returns 200 OK");
        confirmResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ----------------------------------------------------------------
    // GET /api/identity/insights
    // ----------------------------------------------------------------

    [Fact]
    public async Task GetInsights_Returns200_WithArrayBody()
    {
        var response = await _fixture.Client.GetAsync("/api/identity/insights");

        _fixture.LogAssertion("status code is 200 OK");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await _fixture.ReadJsonAsync<JsonElement>(response);

        _fixture.LogAssertion("body is a JSON array");
        body.ValueKind.Should().Be(JsonValueKind.Array);
    }

    // ----------------------------------------------------------------
    // GET /api/identity/snapshots
    // ----------------------------------------------------------------

    [Fact]
    public async Task GetSnapshots_Returns200_WithArrayBody()
    {
        var response = await _fixture.Client.GetAsync("/api/identity/snapshots");

        _fixture.LogAssertion("status code is 200 OK");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await _fixture.ReadJsonAsync<JsonElement>(response);

        _fixture.LogAssertion("body is a JSON array");
        body.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task GetLatestSnapshot_Returns200Or204()
    {
        var response = await _fixture.Client.GetAsync("/api/identity/snapshots/latest");

        // 200 + snapshot object when a snapshot exists.
        // 204 No Content (or 200 with null body) when none exists — ASP.NET Core
        // may emit 204 when Ok(null) is returned from an ActionResult<T?> endpoint.
        _fixture.LogAssertion("status code is 200 or 204");
        ((int)response.StatusCode).Should().BeOneOf(200, 204);
    }
}
