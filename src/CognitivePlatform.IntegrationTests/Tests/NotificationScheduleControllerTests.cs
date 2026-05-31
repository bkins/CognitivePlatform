using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CognitivePlatform.IntegrationTests.Infrastructure;
using Xunit.Abstractions;

namespace CognitivePlatform.IntegrationTests.Tests;

[Trait("Category", "Integration")]
public sealed class NotificationScheduleControllerTests : IDisposable
{
    private readonly ApiFixture _fixture;

    public NotificationScheduleControllerTests(ITestOutputHelper output)
    {
        _fixture = new ApiFixture(output);
    }

    public void Dispose() => _fixture.Dispose();

    // ----------------------------------------------------------------
    // GET /api/notifications/schedule
    // ----------------------------------------------------------------

    [Fact]
    public async Task GetSchedule_Returns200_WithNotificationsArray()
    {
        var response = await _fixture.Client.GetAsync("/api/notifications/schedule");

        _fixture.LogAssertion("status code is 200 OK");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await _fixture.ReadJsonAsync<JsonElement>(response);

        _fixture.LogAssertion("body contains 'notifications' array");
        body.TryGetProperty("notifications", out var notifications).Should().BeTrue(
            "schedule response must contain a 'notifications' property");

        notifications.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task GetSchedule_WithExplicitFromTime_Returns200()
    {
        var fromTime = DateTimeOffset.UtcNow.ToString("o");

        var response = await _fixture.Client.GetAsync(
            $"/api/notifications/schedule?from={Uri.EscapeDataString(fromTime)}");

        _fixture.LogAssertion("status code is 200 OK with explicit ?from= timestamp");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetSchedule_EachNotification_HasRequiredFields()
    {
        var response = await _fixture.Client.GetAsync("/api/notifications/schedule");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body          = await _fixture.ReadJsonAsync<JsonElement>(response);
        var notifications = body.GetProperty("notifications");

        _fixture.LogAssertion("every notification has externalId and title");
        foreach (var notification in notifications.EnumerateArray())
        {
            notification.TryGetProperty("externalId", out _).Should().BeTrue(
                "scheduled notification must have externalId");

            notification.TryGetProperty("title", out _).Should().BeTrue(
                "scheduled notification must have title");
        }
    }

    // ----------------------------------------------------------------
    // POST /api/notifications/feedback
    // ----------------------------------------------------------------

    [Fact]
    public async Task PostFeedback_Returns204_ForValidAction_Tapped()
    {
        var payload = new { ExternalId = Guid.NewGuid().ToString(), Action = "tapped" };

        var response = await _fixture.Client.PostAsJsonAsync(
            "/api/notifications/feedback", payload);

        _fixture.LogAssertion("status code is 204 No Content for action='tapped'");
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task PostFeedback_Returns204_ForValidAction_Dismissed()
    {
        var payload = new { ExternalId = Guid.NewGuid().ToString(), Action = "dismissed" };

        var response = await _fixture.Client.PostAsJsonAsync(
            "/api/notifications/feedback", payload);

        _fixture.LogAssertion("status code is 204 No Content for action='dismissed'");
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task PostFeedback_Returns204_ForValidAction_Acted()
    {
        var payload = new { ExternalId = Guid.NewGuid().ToString(), Action = "acted" };

        var response = await _fixture.Client.PostAsJsonAsync(
            "/api/notifications/feedback", payload);

        _fixture.LogAssertion("status code is 204 No Content for action='acted'");
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task PostFeedback_Returns400_ForUnknownAction()
    {
        var payload = new { ExternalId = Guid.NewGuid().ToString(), Action = "unknown-action" };

        var response = await _fixture.Client.PostAsJsonAsync(
            "/api/notifications/feedback", payload);

        _fixture.LogAssertion("status code is 400 Bad Request for unknown action");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PostFeedback_ActionIsCaseInsensitive()
    {
        var payload = new { ExternalId = Guid.NewGuid().ToString(), Action = "TAPPED" };

        var response = await _fixture.Client.PostAsJsonAsync(
            "/api/notifications/feedback", payload);

        _fixture.LogAssertion("status code is 204 No Content for uppercase 'TAPPED'");
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
