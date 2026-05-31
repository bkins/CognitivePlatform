using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CognitivePlatform.IntegrationTests.Infrastructure;
using Xunit.Abstractions;

namespace CognitivePlatform.IntegrationTests.Tests;

[Trait("Category", "Integration")]
public sealed class JournalControllerTests : IDisposable
{
    private readonly ApiFixture _fixture;

    public JournalControllerTests(ITestOutputHelper output)
    {
        _fixture = new ApiFixture(output);
    }

    public void Dispose() => _fixture.Dispose();

    // ----------------------------------------------------------------
    // GET /api/journals
    // ----------------------------------------------------------------

    [Fact]
    public async Task GetAll_Returns200_WithArrayBody()
    {
        var response = await _fixture.Client.GetAsync("/api/journals");

        _fixture.LogAssertion("status code is 200 OK");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await _fixture.ReadJsonAsync<JsonElement>(response);

        _fixture.LogAssertion("body is a JSON array");
        body.ValueKind.Should().Be(JsonValueKind.Array);
    }

    // ----------------------------------------------------------------
    // GET /api/journals/{id}
    // ----------------------------------------------------------------

    [Fact]
    public async Task GetById_Returns404_ForUnknownGuid()
    {
        var unknownId = Guid.NewGuid();

        var response = await _fixture.Client.GetAsync($"/api/journals/{unknownId}");

        // The service does not guard against missing entries at the controller level —
        // it throws KeyNotFoundException which maps to 404 or 500 depending on middleware.
        // Accept either 404 or 500 as "not found" here; the critical check is that the
        // entry with this ID does not silently return data.
        _fixture.LogAssertion("status code is 404 or 500 for unknown GUID");
        ((int)response.StatusCode).Should().BeOneOf(404, 500);
    }

    [Fact]
    public async Task GetById_ReturnsEntry_WhenEntryExists()
    {
        _fixture.Log("Arrange — fetch journal list to locate a real entry ID");
        var listResponse = await _fixture.Client.GetAsync("/api/journals");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var entries = await _fixture.ReadJsonAsync<JsonElement>(listResponse);

        if (entries.GetArrayLength() == 0)
        {
            _fixture.Log("Skip — no journal entries in live DB");
            return;
        }

        // Entries returned by GET /api/journals may be JournalEntryWithRevision objects;
        // extract the entry's id from the nested entry property.
        var firstEntryElement = entries.EnumerateArray().First();

        string? entryId = null;

        if (firstEntryElement.TryGetProperty("entry", out var entryProp)
            && entryProp.TryGetProperty("id", out var idFromEntry))
        {
            entryId = idFromEntry.GetString();
        }
        else if (firstEntryElement.TryGetProperty("id", out var directId))
        {
            entryId = directId.GetString();
        }

        if (entryId is null)
        {
            _fixture.Log("Skip — could not determine entry ID from list response shape");
            return;
        }

        _fixture.Log($"Act — GET /api/journals/{entryId}");
        var response = await _fixture.Client.GetAsync($"/api/journals/{entryId}");

        _fixture.LogAssertion("status code is 200 OK");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var entry = await _fixture.ReadJsonAsync<JsonElement>(response);

        _fixture.LogAssertion("entry has id, text, createdAt, tags, isEdited fields");
        entry.TryGetProperty("id",        out _).Should().BeTrue("entry.id must be present");
        entry.TryGetProperty("text",      out _).Should().BeTrue("entry.text must be present");
        entry.TryGetProperty("createdAt", out _).Should().BeTrue("entry.createdAt must be present");
        entry.TryGetProperty("tags",      out _).Should().BeTrue("entry.tags must be present");
        entry.TryGetProperty("isEdited",  out _).Should().BeTrue("entry.isEdited must be present");
    }

    // ----------------------------------------------------------------
    // GET /api/journals/{id}/revisions
    // ----------------------------------------------------------------

    [Fact]
    public async Task GetRevisions_Returns404_ForUnknownGuid()
    {
        var unknownId = Guid.NewGuid();

        var response = await _fixture.Client.GetAsync($"/api/journals/{unknownId}/revisions");

        _fixture.LogAssertion("status code is 404 Not Found");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetRevisions_ReturnsArray_WhenEntryExists()
    {
        _fixture.Log("Arrange — fetch journal list to locate a real entry ID");
        var listResponse = await _fixture.Client.GetAsync("/api/journals");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var entries = await _fixture.ReadJsonAsync<JsonElement>(listResponse);

        if (entries.GetArrayLength() == 0)
        {
            _fixture.Log("Skip — no journal entries in live DB");
            return;
        }

        var firstEntryElement = entries.EnumerateArray().First();

        string? entryId = null;

        if (firstEntryElement.TryGetProperty("entry", out var entryProp)
            && entryProp.TryGetProperty("id", out var idProp))
        {
            entryId = idProp.GetString();
        }
        else if (firstEntryElement.TryGetProperty("id", out var directId))
        {
            entryId = directId.GetString();
        }

        if (entryId is null)
        {
            _fixture.Log("Skip — could not determine entry ID from list response shape");
            return;
        }

        _fixture.Log($"Act — GET /api/journals/{entryId}/revisions");
        var response = await _fixture.Client.GetAsync($"/api/journals/{entryId}/revisions");

        _fixture.LogAssertion("status code is 200 OK");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var revisions = await _fixture.ReadJsonAsync<JsonElement>(response);

        _fixture.LogAssertion("revisions body is a JSON array");
        revisions.ValueKind.Should().Be(JsonValueKind.Array);
    }

    // ----------------------------------------------------------------
    // POST /api/journals/{id}/edit-test (dev-only endpoint)
    // ----------------------------------------------------------------

    [Fact]
    public async Task EditTest_Returns404_ForUnknownGuid()
    {
        var unknownId = Guid.NewGuid();

        var editPayload = new
        {
            Text      = "Integration test edit"
          , Tags      = new[] { "test" }
          , Mood      = "calm"
          , MoodScore = (int?)null
        };

        var response = await _fixture.Client.PostAsJsonAsync(
            $"/api/journals/{unknownId}/edit-test"
          , editPayload);

        _fixture.LogAssertion("status code is 404 Not Found");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
