using System.Net;
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

        _fixture.LogAssertion("record has date and isOpen fields");
        record.TryGetProperty("date",   out _).Should().BeTrue("dailyRecord.date must be present");
        record.TryGetProperty("isOpen", out _).Should().BeTrue("dailyRecord.isOpen must be present");
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
}
