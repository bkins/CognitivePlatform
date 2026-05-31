using System.Net;
using System.Text.Json;
using CognitivePlatform.IntegrationTests.Infrastructure;
using Xunit.Abstractions;

namespace CognitivePlatform.IntegrationTests.Tests;

[Trait("Category", "Integration")]
public sealed class KnowledgeControllerTests : IDisposable
{
    private readonly ApiFixture _fixture;

    public KnowledgeControllerTests(ITestOutputHelper output)
    {
        _fixture = new ApiFixture(output);
    }

    public void Dispose() => _fixture.Dispose();

    // ----------------------------------------------------------------
    // GET /api/knowledge
    // ----------------------------------------------------------------

    [Fact]
    public async Task Get_Returns200_WithArrayBody()
    {
        var response = await _fixture.Client.GetAsync("/api/knowledge");

        _fixture.LogAssertion("status code is 200 OK");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await _fixture.ReadJsonAsync<JsonElement>(response);

        _fixture.LogAssertion("body is a JSON array");
        body.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task Get_FilterByKind_Returns200()
    {
        // kind=0 corresponds to the first enum value; API accepts numeric or string values.
        var response = await _fixture.Client.GetAsync("/api/knowledge?kind=0");

        _fixture.LogAssertion("status code is 200 OK for kind=0 filter");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Get_FilterByStatus_Returns200()
    {
        var response = await _fixture.Client.GetAsync("/api/knowledge?status=0");

        _fixture.LogAssertion("status code is 200 OK for status=0 filter");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Get_FilterByTag_Returns200()
    {
        var response = await _fixture.Client.GetAsync("/api/knowledge?tag=test");

        _fixture.LogAssertion("status code is 200 OK for tag=test filter");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Get_FilterBySearch_Returns200()
    {
        var response = await _fixture.Client.GetAsync("/api/knowledge?search=meeting");

        _fixture.LogAssertion("status code is 200 OK for search=meeting filter");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Get_EachItem_HasExpectedFields()
    {
        var response = await _fixture.Client.GetAsync("/api/knowledge");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var items = await _fixture.ReadJsonAsync<JsonElement>(response);

        _fixture.LogAssertion("every knowledge item has an id field");
        foreach (var item in items.EnumerateArray())
        {
            item.TryGetProperty("id", out _).Should().BeTrue("knowledge item must have an id");
        }
    }

    // ----------------------------------------------------------------
    // POST /api/knowledge/{id}/archive
    // ----------------------------------------------------------------

    [Fact]
    public async Task Archive_Returns204Or404_ForRandomId()
    {
        var randomId = Guid.NewGuid();

        var response = await _fixture.Client.PostAsync(
            $"/api/knowledge/{randomId}/archive", content: null);

        // The archive endpoint calls the service without checking existence first.
        // If the service throws on unknown ID it becomes a 500; if it silently
        // no-ops it returns 204. Accept any non-5xx response.
        _fixture.LogAssertion("status code is 204 or 404 (not a server error)");
        ((int)response.StatusCode).Should().BeOneOf(204, 404);
    }
}
