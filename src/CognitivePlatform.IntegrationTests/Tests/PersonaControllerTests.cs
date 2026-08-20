using System.Net;
using System.Net.Http.Json;
using CognitivePlatform.Api.Domains.Personas;
using CognitivePlatform.Api.Domains.Personas.Models;
using CognitivePlatform.IntegrationTests.Infrastructure;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace CognitivePlatform.IntegrationTests.Tests;

[Trait("Category", "Integration")]
public sealed class PersonaControllerTests : IDisposable
{
    private readonly ApiFixture _fixture;

    public PersonaControllerTests(ITestOutputHelper output)
    {
        _fixture = new ApiFixture(output);
    }

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task Create_List_GetById_Memory_Snapshot_Delete_RoundTrip()
    {
        _fixture.Log("Act — POST /api/persona to create new persona");
        var createPayload = new CreatePersonaRequest
                            (
                                Name:                $"TestPersona-{Guid.NewGuid():N}"
                              , ScenarioDescription: "A helpful mentor scenario"
                            );

        var createResponse = await _fixture.Client.PostAsJsonAsync("/api/persona", createPayload);
        var createBody = await createResponse.Content.ReadAsStringAsync();
        createResponse.StatusCode.Should().Be(HttpStatusCode.OK, $"Create returned: {createBody}");

        var persona = await _fixture.ReadJsonAsync<CanonicalPersona>(createResponse);
        persona.Should().NotBeNull();
        var id = persona!.Id;

        _fixture.Log("Act — GET /api/persona");
        var listResponse = await _fixture.Client.GetAsync("/api/persona");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var personas = await _fixture.ReadJsonAsync<List<CanonicalPersona>>(listResponse);
        personas.Should().NotBeNull();
        personas.Should().Contain(item => item.Id == id);

        _fixture.Log($"Act — GET /api/persona/{id}");
        var getResponse = await _fixture.Client.GetAsync($"/api/persona/{id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        _fixture.Log($"Act — POST /api/persona/{id}/memory");
        var memoryPayload = new AddMemoryRequest
                            (
                                Content:      "User enjoys concise technical explanations"
                              , Type:         MemoryType.Narrative
                              , UserAsserted: true
                            );

        var memoryResponse = await _fixture.Client.PostAsJsonAsync($"/api/persona/{id}/memory", memoryPayload);
        memoryResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var memory = await _fixture.ReadJsonAsync<PersonaMemory>(memoryResponse);
        memory.Should().NotBeNull();
        memory!.Content.Should().Be("User enjoys concise technical explanations");

        _fixture.Log($"Act — POST /api/persona/{id}/snapshot");
        var snapshotPayload = new CreateSnapshotRequest
                              (
                                  Name:  "Initial checkpoint"
                                , Notes: "Baseline memory snapshot"
                              );

        var snapshotResponse = await _fixture.Client.PostAsJsonAsync($"/api/persona/{id}/snapshot", snapshotPayload);
        snapshotResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var snapshot = await _fixture.ReadJsonAsync<MemorySnapshot>(snapshotResponse);
        snapshot.Should().NotBeNull();
        snapshot!.Name.Should().Be("Initial checkpoint");

        _fixture.Log($"Act — DELETE /api/persona/{id}");
        var deleteResponse = await _fixture.Client.DeleteAsync($"/api/persona/{id}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        _fixture.Log($"Verify — GET /api/persona/{id} returns 404");
        var getAfterDelete = await _fixture.Client.GetAsync($"/api/persona/{id}");
        getAfterDelete.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
