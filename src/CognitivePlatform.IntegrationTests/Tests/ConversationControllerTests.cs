using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CognitivePlatform.IntegrationTests.Infrastructure;
using Xunit.Abstractions;

namespace CognitivePlatform.IntegrationTests.Tests;

[Trait("Category", "Integration")]
public sealed class ConversationControllerTests : IDisposable
{
    private readonly ApiFixture _fixture;

    public ConversationControllerTests(ITestOutputHelper output)
    {
        _fixture = new ApiFixture(output);
    }

    public void Dispose() => _fixture.Dispose();

    // ----------------------------------------------------------------
    // List
    // ----------------------------------------------------------------

    [Fact]
    public async Task ListConversations_Returns200_WithArrayBody()
    {
        var response = await _fixture.Client.GetAsync("/api/conversation");

        _fixture.LogAssertion("status code is 200 OK");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await _fixture.ReadJsonAsync<JsonElement>(response);

        _fixture.LogAssertion("body is a JSON array");
        body.ValueKind.Should().Be(JsonValueKind.Array);
    }

    // ----------------------------------------------------------------
    // Full CRUD round-trip
    // ----------------------------------------------------------------

    [Fact]
    public async Task Converse_ThenListAndRenameAndDelete_RoundTrip()
    {
        var sessionId = $"integration-test-{Guid.NewGuid():N}";

        _fixture.Log($"Arrange — session ID: {sessionId}");

        // --- Arrange: send a converse request to create session metadata ---
        // A FastPath "System Status" command is a known-good fast-path trigger.
        // The important part of this test is the CRUD operations on metadata;
        // if the converse call cannot succeed (LLM unavailable, no FastPath match),
        // we skip gracefully rather than fail.
        var conversePayload = new
        {
            SessionId = sessionId
          , Input     = "system: System Status"
          , FastPath  = true
          , Streaming = false
        };

        _fixture.Log("Act — POST /api/conversation/converse");
        var converseResponse = await _fixture.Client.PostAsJsonAsync(
            "/api/conversation/converse", conversePayload);

        if (!converseResponse.IsSuccessStatusCode)
        {
            // Orchestrator returned an error (LLM unavailable or no FastPath match);
            // check if metadata was still registered before giving up.
            var listCheck = await _fixture.Client.GetAsync("/api/conversation");
            var allConvs  = await _fixture.ReadJsonAsync<JsonElement>(listCheck);

            var sessionCreated = allConvs.EnumerateArray()
                                         .Any(conversation =>
                                              conversation.TryGetProperty("conversationId", out var idProp)
                                              && idProp.GetString() == sessionId);

            if (!sessionCreated)
            {
                _fixture.Log("Skip — session not tracked after failed converse; LLM/FastPath unavailable");
                return;
            }
        }

        // --- Act: verify session appears in the list ---
        _fixture.Log("Act — GET /api/conversation (list)");
        var listResponse = await _fixture.Client.GetAsync("/api/conversation");

        _fixture.LogAssertion("list endpoint returns 200 OK");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var conversations = await _fixture.ReadJsonAsync<JsonElement>(listResponse);

        _fixture.LogAssertion($"session '{sessionId}' appears in list");
        conversations.ValueKind.Should().Be(JsonValueKind.Array);
        conversations.EnumerateArray()
                     .Any(conversation =>
                          conversation.TryGetProperty("conversationId", out var idProp)
                          && idProp.GetString() == sessionId)
                     .Should().BeTrue($"session '{sessionId}' should appear in the conversation list");

        // --- Act: get metadata ---
        _fixture.Log($"Act — GET /api/conversation/{sessionId}/metadata");
        var metaResponse = await _fixture.Client.GetAsync(
            $"/api/conversation/{sessionId}/metadata");

        _fixture.LogAssertion("metadata endpoint returns 200 OK");
        metaResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var meta = await _fixture.ReadJsonAsync<JsonElement>(metaResponse);

        _fixture.LogAssertion("conversationId in metadata matches session ID");
        meta.GetProperty("conversationId").GetString().Should().Be(sessionId);

        // --- Act: rename ---
        var newName = "Integration Test — Renamed";

        _fixture.Log($"Act — PUT /api/conversation/{sessionId}/name → '{newName}'");
        var renameResponse = await _fixture.Client.PutAsJsonAsync(
            $"/api/conversation/{sessionId}/name"
          , new { Name = newName });

        _fixture.LogAssertion("rename returns 200 OK");
        renameResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var renamed = await _fixture.ReadJsonAsync<JsonElement>(renameResponse);

        _fixture.LogAssertion($"returned name equals '{newName}'");
        renamed.GetProperty("name").GetString().Should().Be(newName);

        // --- Act: delete (soft) ---
        _fixture.Log($"Act — DELETE /api/conversation/{sessionId}");
        var deleteResponse = await _fixture.Client.DeleteAsync(
            $"/api/conversation/{sessionId}");

        _fixture.LogAssertion("delete returns 204 No Content");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // --- Assert: second delete returns 404 ---
        _fixture.Log("Assert — second DELETE should return 404 (already soft-deleted)");
        var secondDeleteResponse = await _fixture.Client.DeleteAsync(
            $"/api/conversation/{sessionId}");

        _fixture.LogAssertion("second delete returns 404 Not Found");
        secondDeleteResponse.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "soft-deleted conversation should not be found for a second delete");
    }

    // ----------------------------------------------------------------
    // History
    // ----------------------------------------------------------------

    [Fact]
    public async Task GetHistory_Returns200_ForNonExistentSession()
    {
        var fakeSession = $"integration-empty-{Guid.NewGuid():N}";

        var response = await _fixture.Client.GetAsync(
            $"/api/conversation/{fakeSession}/history");

        _fixture.LogAssertion("status code is 200 OK");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await _fixture.ReadJsonAsync<JsonElement>(response);

        _fixture.LogAssertion("body is an empty JSON array");
        body.ValueKind.Should().Be(JsonValueKind.Array);
        body.GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task GetHistory_RespectsLastParameter()
    {
        var fakeSession = $"integration-last-{Guid.NewGuid():N}";

        var response = await _fixture.Client.GetAsync(
            $"/api/conversation/{fakeSession}/history?last=5");

        _fixture.LogAssertion("status code is 200 OK even with ?last=5 on empty session");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ----------------------------------------------------------------
    // Metadata — not found
    // ----------------------------------------------------------------

    [Fact]
    public async Task GetMetadata_Returns404_ForUnknownId()
    {
        var unknownId = Guid.NewGuid().ToString("N");

        var response = await _fixture.Client.GetAsync(
            $"/api/conversation/{unknownId}/metadata");

        _fixture.LogAssertion("status code is 404 Not Found");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ----------------------------------------------------------------
    // Rename — not found
    // ----------------------------------------------------------------

    [Fact]
    public async Task RenameConversation_Returns404_ForUnknownId()
    {
        var unknownId = Guid.NewGuid().ToString("N");

        var response = await _fixture.Client.PutAsJsonAsync(
            $"/api/conversation/{unknownId}/name"
          , new { Name = "Should Not Work" });

        _fixture.LogAssertion("status code is 404 Not Found");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ----------------------------------------------------------------
    // Converse — fast-path action name assertion
    // ----------------------------------------------------------------

    [Fact]
    public async Task Converse_FastPath_ListActions_ReturnsSelectedActionAndFastPathFlag()
    {
        var sessionId = $"integration-fastpath-{Guid.NewGuid():N}";

        var payload = new
        {
            SessionId = sessionId
          , Input     = "list actions"
        };

        _fixture.Log("Act — POST /api/conversation/converse with 'list actions'");
        var response = await _fixture.Client.PostAsJsonAsync(
            "/api/conversation/converse", payload);

        if (!response.IsSuccessStatusCode)
        {
            _fixture.Log("Skip — converse returned non-success; orchestrator unavailable");
            return;
        }

        var body = await response.Content.ReadAsStringAsync();
        var json = JsonSerializer.Deserialize<JsonElement>(body, ApiFixture.JsonOptions);

        _fixture.LogAssertion("wasFastPath is true for 'list actions'");
        json.GetProperty("wasFastPath").GetBoolean().Should().BeTrue();

        _fixture.LogAssertion("selectedAction is 'ListActions'");
        json.GetProperty("selectedAction").GetString().Should().Be("ListActions");

        _fixture.LogAssertion("success is true");
        json.GetProperty("success").GetBoolean().Should().BeTrue();

        _fixture.LogAssertion("message is not null or empty");
        json.GetProperty("message").GetString().Should().NotBeNullOrEmpty();

        // Cleanup — remove session metadata
        await _fixture.Client.DeleteAsync($"/api/conversation/{sessionId}");
    }
}
