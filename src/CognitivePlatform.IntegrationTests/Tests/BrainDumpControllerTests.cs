using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CognitivePlatform.Api.Controllers;
using CognitivePlatform.Api.Domains.BrainDump;
using CognitivePlatform.IntegrationTests.Infrastructure;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace CognitivePlatform.IntegrationTests.Tests;

[Trait("Category", "Integration")]
public sealed class BrainDumpControllerTests : IDisposable
{
    private readonly ApiFixture _fixture;

    public BrainDumpControllerTests(ITestOutputHelper output)
    {
        _fixture = new ApiFixture(output);
    }

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task StartSession_ReturnsCreatedSession()
    {
        _fixture.Log("Act — POST /api/braindumps to start new session");
        var response = await _fixture.Client.PostAsync("/api/braindumps", null);

        _fixture.LogAssertion("returns 201 Created with session details");
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var session = await _fixture.ReadJsonAsync<BrainDumpSession>(response);
        session.Should().NotBeNull();
        session!.Id.Should().NotBeNullOrWhiteSpace();
        session.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task List_ReturnsBrainDumpSessions()
    {
        _fixture.Log("Arrange — start session");
        await _fixture.Client.PostAsync("/api/braindumps", null);

        _fixture.Log("Act — GET /api/braindumps");
        var response = await _fixture.Client.GetAsync("/api/braindumps");

        _fixture.LogAssertion("returns 200 OK with list of sessions");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var sessions = await _fixture.ReadJsonAsync<List<BrainDumpSession>>(response);
        sessions.Should().NotBeNull();
        sessions!.Count.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task BrainDump_FullLifecycle_UpdateProcessDelete_RoundTrip()
    {
        _fixture.Log("Arrange — start session");
        var startResponse = await _fixture.Client.PostAsync("/api/braindumps", null);
        startResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var session = await _fixture.ReadJsonAsync<BrainDumpSession>(startResponse);
        session.Should().NotBeNull();
        var id = session!.Id;

        _fixture.Log($"Act — PATCH /api/braindumps/{id} with categories");
        var updatePayload = new UpdateBrainDumpRequest
                            {
                                Avoidance       = "Taxes and dentist"
                              , Fears           = "Running out of time"
                              , Frustrations    = "Build latency"
                              , Discouragements = "Slow progress"
                              , SelfCriticism   = "Should be faster"
                            };

        var updateResponse = await _fixture.Client.PatchAsJsonAsync($"/api/braindumps/{id}", updatePayload);
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await _fixture.ReadJsonAsync<BrainDumpSession>(updateResponse);
        updated.Should().NotBeNull();
        updated!.Avoidance.Should().Be("Taxes and dentist");
        updated.Fears.Should().Be("Running out of time");

        _fixture.Log($"Act — POST /api/braindumps/{id}/process");
        var processPayload = new ProcessBrainDumpRequest
                             {
                                 ExtractionSummary   = "Extracted 2 tasks and 1 insight"
                               , ExtractedTaskIds   = new[] { "task-1", "task-2" }
                               , ExtractedInsightIds = new[] { "insight-1" }
                             };

        var processResponse = await _fixture.Client.PostAsJsonAsync($"/api/braindumps/{id}/process", processPayload);
        processResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var processed = await _fixture.ReadJsonAsync<BrainDumpSession>(processResponse);
        processed.Should().NotBeNull();
        processed!.Processed.Should().BeTrue();
        processed.ExtractionSummary.Should().Be("Extracted 2 tasks and 1 insight");

        _fixture.Log($"Act — DELETE /api/braindumps/{id}");
        var deleteResponse = await _fixture.Client.DeleteAsync($"/api/braindumps/{id}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        _fixture.Log($"Verify — GET /api/braindumps/{id} returns 404");
        var getAfterDelete = await _fixture.Client.GetAsync($"/api/braindumps/{id}");
        getAfterDelete.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
