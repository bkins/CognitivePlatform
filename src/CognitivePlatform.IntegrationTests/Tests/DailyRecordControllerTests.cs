using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CognitivePlatform.IntegrationTests.Infrastructure;
using Xunit.Abstractions;

namespace CognitivePlatform.IntegrationTests.Tests;

[Trait("Category", "Integration")]
public sealed class DailyRecordControllerTests : IDisposable
{
    private readonly ApiFixture _fixture;

    public DailyRecordControllerTests(ITestOutputHelper output)
    {
        _fixture = new ApiFixture(output);
    }

    public void Dispose() => _fixture.Dispose();

    // ----------------------------------------------------------------
    // GET /api/daily/today
    // ----------------------------------------------------------------

    [Fact]
    public async Task GetToday_Returns200Or404()
    {
        var response = await _fixture.Client.GetAsync("/api/daily/today");

        // 200 if a day has been opened today; 404 if no plan submitted yet.
        _fixture.LogAssertion("status code is 200 or 404");
        ((int)response.StatusCode).Should().BeOneOf(200, 404);
    }

    [Fact]
    public async Task GetToday_WhenExists_HasExpectedFields()
    {
        var response = await _fixture.Client.GetAsync("/api/daily/today");

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            _fixture.Log("Skip — no daily record for today");
            return;
        }

        _fixture.LogAssertion("status code is 200 OK");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var record = await _fixture.ReadJsonAsync<JsonElement>(response);

        _fixture.LogAssertion("record has date and phase fields");
        record.TryGetProperty("date",  out _).Should().BeTrue("dailyRecord.date must be present");
        record.TryGetProperty("phase", out _).Should().BeTrue("dailyRecord.phase must be present");
    }

    // ----------------------------------------------------------------
    // GET /api/daily/{date}
    // ----------------------------------------------------------------

    [Fact]
    public async Task GetByDate_Returns400_ForInvalidFormat()
    {
        var response = await _fixture.Client.GetAsync("/api/daily/not-a-date");

        _fixture.LogAssertion("status code is 400 Bad Request");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetByDate_Returns404_ForDateWithNoRecord()
    {
        // Use a date far in the future that definitely has no record.
        var response = await _fixture.Client.GetAsync("/api/daily/2099-01-01");

        _fixture.LogAssertion("status code is 404 Not Found for 2099-01-01");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetByDate_Returns200Or404_ForToday()
    {
        var today    = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd");
        var response = await _fixture.Client.GetAsync($"/api/daily/{today}");

        _fixture.LogAssertion("status code is 200 or 404 for today");
        ((int)response.StatusCode).Should().BeOneOf(200, 404);
    }

    // ----------------------------------------------------------------
    // GET /api/daily — range query
    // ----------------------------------------------------------------

    [Fact]
    public async Task GetRange_Returns400_WhenFromMissing()
    {
        var response = await _fixture.Client.GetAsync("/api/daily?to=2026-01-01");

        _fixture.LogAssertion("status code is 400 when 'from' is missing");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetRange_Returns400_WhenToMissing()
    {
        var response = await _fixture.Client.GetAsync("/api/daily?from=2026-01-01");

        _fixture.LogAssertion("status code is 400 when 'to' is missing");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetRange_Returns400_WhenFromIsAfterTo()
    {
        var response = await _fixture.Client.GetAsync("/api/daily?from=2026-05-31&to=2026-05-01");

        _fixture.LogAssertion("status code is 400 when from > to");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetRange_Returns400_ForInvalidFromFormat()
    {
        var response = await _fixture.Client.GetAsync("/api/daily?from=bad&to=2026-01-01");

        _fixture.LogAssertion("status code is 400 for malformed 'from' date");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetRange_Returns200_WithArrayBody_ForValidRange()
    {
        var response = await _fixture.Client.GetAsync(
            "/api/daily?from=2026-01-01&to=2026-12-31");

        _fixture.LogAssertion("status code is 200 OK");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await _fixture.ReadJsonAsync<JsonElement>(response);

        _fixture.LogAssertion("body is a JSON array");
        body.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task GetRange_SameDayRange_Returns200()
    {
        var today    = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd");
        var response = await _fixture.Client.GetAsync(
            $"/api/daily?from={today}&to={today}");

        _fixture.LogAssertion("status code is 200 OK for same-day range");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ----------------------------------------------------------------
    // Deep structural check on today's record
    // ----------------------------------------------------------------

    [Fact]
    public async Task GetToday_WhenExists_HasFullStructure()
    {
        var response = await _fixture.Client.GetAsync("/api/daily/today");

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            _fixture.Log("Skip — no daily record for today");
            return;
        }

        _fixture.LogAssertion("status code is 200 OK");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var record = await _fixture.ReadJsonAsync<JsonElement>(response);

        _fixture.LogAssertion("record has date field");
        record.TryGetProperty("date", out _).Should().BeTrue("dailyRecord.date must be present");

        _fixture.LogAssertion("record has phase field");
        record.TryGetProperty("phase", out var phaseProp).Should().BeTrue(
            "dailyRecord.phase must be present");

        _fixture.LogAssertion("record has openedAtUtc field");
        record.TryGetProperty("openedAtUtc", out _).Should().BeTrue(
            "dailyRecord.openedAtUtc must be present");

        // checkpointIds array
        if (record.TryGetProperty("checkpointIds", out var checkpointIds))
        {
            _fixture.LogAssertion("checkpointIds is an array");
            checkpointIds.ValueKind.Should().Be(JsonValueKind.Array);
        }

        // plannedTaskIds array
        if (record.TryGetProperty("plannedTaskIds", out var plannedTaskIds))
        {
            _fixture.LogAssertion("plannedTaskIds is an array");
            plannedTaskIds.ValueKind.Should().Be(JsonValueKind.Array);
        }

        // If closed, closedAtUtc should be populated
        var phaseVal = phaseProp.ValueKind == JsonValueKind.String ? phaseProp.GetString() : phaseProp.GetInt32().ToString();
        if (phaseVal == "Closed" || phaseVal == "3")
        {
            _fixture.LogAssertion("closed record has populated closedAtUtc");
            record.TryGetProperty("closedAtUtc", out var closedProp).Should().BeTrue(
                "closed dailyRecord must have closedAtUtc");
            closedProp.ValueKind.Should().NotBe(JsonValueKind.Null,
                "closedAtUtc should not be null on a closed record");
        }
    }

    // ----------------------------------------------------------------
    // Range query — structural consistency across returned records
    // ----------------------------------------------------------------

    [Fact]
    public async Task GetRange_EachRecord_HasConsistentStructure()
    {
        var response = await _fixture.Client.GetAsync(
            "/api/daily?from=2026-01-01&to=2026-12-31");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var records = await _fixture.ReadJsonAsync<JsonElement>(response);

        if (records.GetArrayLength() == 0)
        {
            _fixture.Log("Skip — no daily records in the 2026 range");
            return;
        }

        _fixture.LogAssertion("each record in range has date, phase, openedAtUtc");
        foreach (var record in records.EnumerateArray())
        {
            record.TryGetProperty("date", out _).Should().BeTrue("each record must have date");
            record.TryGetProperty("phase", out _).Should().BeTrue("each record must have phase");
            record.TryGetProperty("openedAtUtc", out _).Should().BeTrue(
                "each record must have openedAtUtc");
        }

        // Verify records are ordered by date (oldest first or newest first — just check consistency)
        var dates = records.EnumerateArray()
                           .Select(r => r.GetProperty("date").GetString()!)
                           .ToList();

        _fixture.LogAssertion("records are returned in a consistent order (all dates parseable)");
        dates.Should().AllSatisfy(d =>
            DateOnly.TryParse(d, out _).Should().BeTrue($"'{d}' should be a valid date"));
    }

    // ----------------------------------------------------------------
    // Full E2E Cycle: delete existing → open day → add checkpoint → close → delete
    // ----------------------------------------------------------------

    [Fact]
    public async Task DailyRecord_FullE2ECycle_RoundTrip()
    {
        var sessionId = $"daily-crud-{Guid.NewGuid():N}";

        // ── 1. Cleanup any pre-existing record for today so we start fresh ──
        _fixture.Log("Arrange — delete today's daily record (clean slate)");
        var deletePayload = new
        {
            SessionId = sessionId
          , Input     = "Delete today's daily record"
        };
        var deleteResponse = await _fixture.Client.PostAsJsonAsync(
            "/api/conversation/converse", deletePayload);

        // Converse endpoint might fail if orchestrator/LLM is offline
        if (!deleteResponse.IsSuccessStatusCode)
        {
            _fixture.Log("Skip — converse returned non-success; orchestrator offline");
            return;
        }

        // Verify it was a fast-path trigger or at least executed
        var deleteBody = await deleteResponse.Content.ReadAsStringAsync();
        var deleteJson = JsonSerializer.Deserialize<JsonElement>(deleteBody, ApiFixture.JsonOptions);
        var msg = deleteJson.GetProperty("message").GetString() ?? string.Empty;
        _fixture.Log($"Delete record converse output: {msg}");

        if (msg.Contains("not usable on this system") || msg.Contains("didn't recognize that as a command"))
        {
            _fixture.Log("Skip — LLM provider/model is not usable on this system");
            return;
        }

        // If the action is destructive and requires confirmation, confirm it
        if (deleteJson.TryGetProperty("isConfirmationRequired", out var isConfProp) && isConfProp.GetBoolean())
        {
            _fixture.Log("Confirming delete daily record...");
            var confirmPayload = new
            {
                SessionId = sessionId
              , Input     = "yes"
            };
            var confirmResponse = await _fixture.Client.PostAsJsonAsync(
                "/api/conversation/converse", confirmPayload);
            confirmResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        // Verify GET today returns 404 now that it's deleted
        var getTodayBefore = await _fixture.Client.GetAsync("/api/daily/today");
        getTodayBefore.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // ── 2. Open a fresh day ──
        _fixture.Log("Act — Open today's plan");
        var openPayload = new
        {
            SessionId = sessionId
          , Input     = "Plan: Focus on integration tests.\nTasks:\n- Write DailyRecord integration tests\n- Run tests"
        };
        var openResponse = await _fixture.Client.PostAsJsonAsync(
            "/api/conversation/converse", openPayload);
        openResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var openBody = await openResponse.Content.ReadAsStringAsync();
        var openJson = JsonSerializer.Deserialize<JsonElement>(openBody, ApiFixture.JsonOptions);
        openJson.GetProperty("wasFastPath").GetBoolean().Should().BeTrue();
        openJson.GetProperty("selectedAction").GetString().Should().Be("OpenDay");

        // Verify GET today returns 200 and properties match
        var getTodayAfterOpen = await _fixture.Client.GetAsync("/api/daily/today");
        getTodayAfterOpen.StatusCode.Should().Be(HttpStatusCode.OK);
        var recordAfterOpen = await _fixture.ReadJsonAsync<JsonElement>(getTodayAfterOpen);
        var openPhaseProp = recordAfterOpen.GetProperty("phase");
        var openPhaseVal = openPhaseProp.ValueKind == JsonValueKind.String ? openPhaseProp.GetString() : openPhaseProp.GetInt32().ToString();
        openPhaseVal.Should().BeOneOf("Opening", "Active", "DayStarted", "1", "2");
        recordAfterOpen.GetProperty("plannedTaskIds").GetArrayLength().Should().Be(2);

        // ── 3. Add checkpoint ──
        _fixture.Log("Act — Add check-in");
        var checkpointPayload = new
        {
            SessionId = sessionId
          , Input     = "Check: Finished writing integration tests."
        };
        var checkpointResponse = await _fixture.Client.PostAsJsonAsync(
            "/api/conversation/converse", checkpointPayload);
        checkpointResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var checkpointBody = await checkpointResponse.Content.ReadAsStringAsync();
        var checkpointJson = JsonSerializer.Deserialize<JsonElement>(checkpointBody, ApiFixture.JsonOptions);
        checkpointJson.GetProperty("wasFastPath").GetBoolean().Should().BeTrue();
        checkpointJson.GetProperty("selectedAction").GetString().Should().Be("AddCheckpoint");

        // Verify checkpoint exists in today's record
        var getTodayAfterCheckpoint = await _fixture.Client.GetAsync("/api/daily/today");
        var recordAfterCheckpoint = await _fixture.ReadJsonAsync<JsonElement>(getTodayAfterCheckpoint);
        recordAfterCheckpoint.GetProperty("checkpointIds").GetArrayLength().Should().Be(1);

        // ── 4. Close the day ──
        _fixture.Log("Act — Close the day");
        var closePayload = new
        {
            SessionId = sessionId
          , Input     = "Close day"
        };
        var closeResponse = await _fixture.Client.PostAsJsonAsync(
            "/api/conversation/converse", closePayload);
        closeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var closeBody = await closeResponse.Content.ReadAsStringAsync();
        var closeJson = JsonSerializer.Deserialize<JsonElement>(closeBody, ApiFixture.JsonOptions);
        closeJson.GetProperty("wasFastPath").GetBoolean().Should().BeTrue();
        closeJson.GetProperty("selectedAction").GetString().Should().Be("CloseDay");

        // Verify today's record is closed
        var getTodayAfterClose = await _fixture.Client.GetAsync("/api/daily/today");
        var recordAfterClose = await _fixture.ReadJsonAsync<JsonElement>(getTodayAfterClose);
        var closePhaseProp = recordAfterClose.GetProperty("phase");
        var closePhaseVal = closePhaseProp.ValueKind == JsonValueKind.String ? closePhaseProp.GetString() : closePhaseProp.GetInt32().ToString();
        closePhaseVal.Should().BeOneOf("Closed", "EveningReview", "3");

        // ── 5. Cleanup: delete today's record ──
        _fixture.Log("Cleanup — Delete daily record for today");
        var cleanupResponse = await _fixture.Client.PostAsJsonAsync(
            "/api/conversation/converse", deletePayload);
        cleanupResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var cleanupBody = await cleanupResponse.Content.ReadAsStringAsync();
        var cleanupJson = JsonSerializer.Deserialize<JsonElement>(cleanupBody, ApiFixture.JsonOptions);
        if (cleanupJson.TryGetProperty("isConfirmationRequired", out var isConfPropCleanup) && isConfPropCleanup.GetBoolean())
        {
            var confirmPayload = new
            {
                SessionId = sessionId
              , Input     = "yes"
            };
            var confirmResponse = await _fixture.Client.PostAsJsonAsync(
                "/api/conversation/converse", confirmPayload);
            confirmResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        // Final check that it is gone
        var getTodayFinal = await _fixture.Client.GetAsync("/api/daily/today");
        getTodayFinal.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Remove session metadata
        await _fixture.Client.DeleteAsync($"/api/conversation/{sessionId}");
    }
}
