using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CognitivePlatform.IntegrationTests.Infrastructure;
using Xunit.Abstractions;

namespace CognitivePlatform.IntegrationTests.Tests;

[Trait("Category", "Integration")]
public sealed class TaskControllerTests : IDisposable
{
    private readonly ApiFixture _fixture;

    public TaskControllerTests(ITestOutputHelper output)
    {
        _fixture = new ApiFixture(output);
    }

    public void Dispose() => _fixture.Dispose();

    // ----------------------------------------------------------------
    // GET /api/tasks — active list
    // ----------------------------------------------------------------

    [Fact]
    public async Task GetActive_Returns200_WithArrayBody()
    {
        var response = await _fixture.Client.GetAsync("/api/tasks");

        _fixture.LogAssertion("status code is 200 OK");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await _fixture.ReadJsonAsync<JsonElement>(response);

        _fixture.LogAssertion("body is a JSON array");
        body.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task GetActive_EachItem_HasExpectedFields()
    {
        var response = await _fixture.Client.GetAsync("/api/tasks");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var tasks = await _fixture.ReadJsonAsync<JsonElement>(response);

        _fixture.LogAssertion("every task has id, shortDescription, priority, createdAt");
        foreach (var task in tasks.EnumerateArray())
        {
            task.TryGetProperty("id",               out _).Should().BeTrue("task.id must be present");
            task.TryGetProperty("shortDescription", out _).Should().BeTrue("task.shortDescription must be present");
            task.TryGetProperty("priority",         out _).Should().BeTrue("task.priority must be present");
            task.TryGetProperty("createdAt",        out _).Should().BeTrue("task.createdAt must be present");
        }
    }

    // ----------------------------------------------------------------
    // GET /api/tasks/{id} — by ID
    // ----------------------------------------------------------------

    [Fact]
    public async Task GetById_Returns404_ForUnknownGuid()
    {
        var unknownId = Guid.NewGuid();

        var response = await _fixture.Client.GetAsync($"/api/tasks/{unknownId}");

        _fixture.LogAssertion("status code is 404 Not Found");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ----------------------------------------------------------------
    // GET /api/tasks/brief
    // ----------------------------------------------------------------

    [Fact]
    public async Task GetBrief_Returns200_WithStringBody()
    {
        var response = await _fixture.Client.GetAsync("/api/tasks/brief");

        _fixture.LogAssertion("status code is 200 OK");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetBrief_WithValidDate_Returns200()
    {
        var date     = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd");
        var response = await _fixture.Client.GetAsync($"/api/tasks/brief?date={date}");

        _fixture.LogAssertion("status code is 200 OK for today's date");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetBrief_WithInvalidDate_Returns400()
    {
        var response = await _fixture.Client.GetAsync("/api/tasks/brief?date=not-a-date");

        _fixture.LogAssertion("status code is 400 Bad Request for non-date string");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ----------------------------------------------------------------
    // DELETE /api/tasks/{id} and PUT /api/tasks/{id} (undelete)
    // ----------------------------------------------------------------

    [Fact]
    public async Task Delete_Returns404_ForUnknownGuid()
    {
        var unknownId = Guid.NewGuid();

        var response = await _fixture.Client.DeleteAsync($"/api/tasks/{unknownId}");

        _fixture.LogAssertion("status code is 404 Not Found");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Undelete_Returns404_ForUnknownGuid()
    {
        var unknownId = Guid.NewGuid();

        var response = await _fixture.Client.PutAsync(
            $"/api/tasks/{unknownId}", content: null);

        _fixture.LogAssertion("status code is 404 Not Found");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ----------------------------------------------------------------
    // Round-trip: delete then undelete a real active task (if any)
    // ----------------------------------------------------------------

    [Fact]
    public async Task DeleteAndUndelete_RoundTrip_WhenActiveTasks_Exist()
    {
        _fixture.Log("Arrange — fetch active task list");
        var listResponse = await _fixture.Client.GetAsync("/api/tasks");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var tasks = await _fixture.ReadJsonAsync<JsonElement>(listResponse);

        if (tasks.GetArrayLength() == 0)
        {
            _fixture.Log("Skip — no active tasks in live DB");
            return;
        }

        var firstTask = tasks.EnumerateArray().First();
        var taskId    = firstTask.GetProperty("id").GetString()!;

        _fixture.Log($"Act — DELETE /api/tasks/{taskId}");
        var deleteResponse = await _fixture.Client.DeleteAsync($"/api/tasks/{taskId}");

        _fixture.LogAssertion("delete returns 200 OK");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        _fixture.Log("Assert — task no longer appears in GET by ID");
        var afterDeleteResponse = await _fixture.Client.GetAsync($"/api/tasks/{taskId}");

        _fixture.LogAssertion("GET by ID returns 404 after delete");
        afterDeleteResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        _fixture.Log($"Cleanup — PUT /api/tasks/{taskId} (undelete)");
        var undeleteResponse = await _fixture.Client.PutAsync(
            $"/api/tasks/{taskId}", content: null);

        _fixture.LogAssertion("undelete returns 200 OK");
        undeleteResponse.StatusCode.Should().Be(HttpStatusCode.OK);
 
        _fixture.Log("Assert — task visible again after undelete");
        var afterUndeleteResponse = await _fixture.Client.GetAsync($"/api/tasks/{taskId}");
 
        _fixture.LogAssertion("GET by ID returns 200 OK after undelete");
        afterUndeleteResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ----------------------------------------------------------------
    // POST /api/tasks/{id}/edit
    // ----------------------------------------------------------------

    [Fact]
    public async Task Edit_Returns200_WithUpdatedTask_WhenActiveTasks_Exist()
    {
        _fixture.Log("Arrange — fetch active task list");
        var listResponse = await _fixture.Client.GetAsync("/api/tasks");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var tasks = await _fixture.ReadJsonAsync<JsonElement>(listResponse);

        if (tasks.GetArrayLength() == 0)
        {
            _fixture.Log("Skip — no active tasks in live DB");
            return;
        }

        var firstTask = tasks.EnumerateArray().First();
        var taskId    = firstTask.GetProperty("id").GetString()!;

        // Let's generate a unique description so we can verify it
        var testGuid = Guid.NewGuid().ToString("N");
        var newDesc  = $"Updated Task Description {testGuid}";
        var newDetails = $"Some details {testGuid}";
        var newTags = new[] { "tag1", "tag2" };
        var newDueDate = DateTimeOffset.UtcNow.AddDays(3);
        var newCompletedAt = DateTimeOffset.UtcNow.AddDays(1);

        var payload = new
        {
            ShortDescription = newDesc,
            Details = newDetails,
            Tags = newTags,
            DueDate = newDueDate,
            CompletedAt = newCompletedAt
        };

        _fixture.Log($"Act — POST /api/tasks/{taskId}/edit");
        var editResponse = await _fixture.Client.PostAsJsonAsync($"/api/tasks/{taskId}/edit", payload);

        _fixture.LogAssertion("edit returns 200 OK");
        editResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var updatedTask = await _fixture.ReadJsonAsync<JsonElement>(editResponse);
        updatedTask.GetProperty("shortDescription").GetString().Should().Be(newDesc);
        updatedTask.GetProperty("details").GetString().Should().Be(newDetails);
        
        var tagsElement = updatedTask.GetProperty("tags");
        tagsElement.ValueKind.Should().Be(JsonValueKind.Array);
        tagsElement.GetArrayLength().Should().Be(2);

        updatedTask.GetProperty("dueDate").GetDateTimeOffset().Date.Should().Be(newDueDate.Date);
        updatedTask.GetProperty("completedAt").GetDateTimeOffset().Date.Should().Be(newCompletedAt.Date);
    }

    // ----------------------------------------------------------------
    // Full CRUD: create via converse → list → complete → verify → cleanup
    // ----------------------------------------------------------------

    [Fact]
    public async Task CreateViaConverse_ThenComplete_ThenCleanup_RoundTrip()
    {
        var sessionId    = $"task-crud-{Guid.NewGuid():N}";
        var uniqueMarker = $"IntTest-{Guid.NewGuid():N}";

        // ── Create via converse fast-path ──
        _fixture.Log($"Arrange — create task via converse (marker: {uniqueMarker})");
        var conversePayload = new
        {
            SessionId = sessionId
          , Input     = $"task: {uniqueMarker}"
        };

        var converseResponse = await _fixture.Client.PostAsJsonAsync(
            "/api/conversation/converse", conversePayload);

        if (!converseResponse.IsSuccessStatusCode)
        {
            _fixture.Log("Skip — converse returned non-success; orchestrator unavailable");
            return;
        }

        // Verify converse reported fast-path success
        var converseBody = await converseResponse.Content.ReadAsStringAsync();
        var converseJson = JsonSerializer.Deserialize<JsonElement>(converseBody, ApiFixture.JsonOptions);

        _fixture.LogAssertion("converse reports wasFastPath = true");
        converseJson.GetProperty("wasFastPath").GetBoolean().Should().BeTrue();

        _fixture.LogAssertion("converse reports selectedAction = AddTask");
        converseJson.GetProperty("selectedAction").GetString().Should().Be("AddTask");

        // ── Find created task in the active list ──
        _fixture.Log("Act — GET /api/tasks to find created task");
        var listResponse = await _fixture.Client.GetAsync("/api/tasks");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var tasks = await _fixture.ReadJsonAsync<JsonElement>(listResponse);

        var createdTask = tasks.EnumerateArray()
                               .FirstOrDefault(t =>
                                   t.TryGetProperty("shortDescription", out var desc)
                                   && (desc.GetString()?.Contains(uniqueMarker
                                          , StringComparison.OrdinalIgnoreCase) ?? false));

        _fixture.LogAssertion($"task containing '{uniqueMarker}' appears in active list");
        createdTask.ValueKind.Should().NotBe(JsonValueKind.Undefined,
            $"task with marker '{uniqueMarker}' should appear in GET /api/tasks");

        var taskId = createdTask.GetProperty("id").GetString()!;

        // ── Complete via edit endpoint ──
        _fixture.Log($"Act — POST /api/tasks/{taskId}/edit (set CompletedAt)");
        var completedAt = DateTimeOffset.UtcNow;
        var editPayload = new
        {
            ShortDescription = uniqueMarker
          , CompletedAt      = completedAt
        };

        var editResponse = await _fixture.Client.PostAsJsonAsync(
            $"/api/tasks/{taskId}/edit", editPayload);

        _fixture.LogAssertion("edit returns 200 OK");
        editResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var editedTask = await _fixture.ReadJsonAsync<JsonElement>(editResponse);

        _fixture.LogAssertion("completedAt is set after edit");
        editedTask.GetProperty("completedAt").ValueKind.Should().NotBe(JsonValueKind.Null,
            "completedAt should be set after completing the task");

        // ── Cleanup: delete the test task ──
        _fixture.Log($"Cleanup — DELETE /api/tasks/{taskId}");
        var deleteResponse = await _fixture.Client.DeleteAsync($"/api/tasks/{taskId}");

        _fixture.LogAssertion("delete returns 200 OK");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        _fixture.Log("Assert — task no longer in active list after delete");
        var afterDelete = await _fixture.Client.GetAsync($"/api/tasks/{taskId}");
        afterDelete.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Cleanup — remove session metadata
        await _fixture.Client.DeleteAsync($"/api/conversation/{sessionId}");
    }
}
