using System.Net;
using System.Net.Http.Headers;
using CognitivePlatform.IntegrationTests.Infrastructure;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace CognitivePlatform.IntegrationTests.Tests;

[Trait("Category", "Integration")]
public sealed class AdminControllersTests : IDisposable
{
    private readonly ApiFixture _fixture;

    public AdminControllersTests(ITestOutputHelper output)
    {
        _fixture = new ApiFixture(output);
    }

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task AdminRegistry_Unauthorized_WithoutHeader()
    {
        _fixture.Log("Act — GET /api/admin/registry without secret header");
        using var unauthClient = _fixture.CreateClientWithoutAdminHeader();
        var response = await unauthClient.GetAsync("/api/admin/registry");

        _fixture.LogAssertion("returns 401 Unauthorized");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AdminRegistry_Authorized_ReturnsActionsList()
    {
        _fixture.Log("Act — GET /api/admin/registry with X-Admin-Secret header");
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/registry");
        request.Headers.Add("X-Admin-Secret", ApiFixture.AdminSecret);

        var response = await _fixture.Client.SendAsync(request);

        _fixture.LogAssertion("returns 200 OK with registered actions");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("AddJournalEntry");
        body.Should().Contain("AddTask");
    }

    [Fact]
    public async Task AdminSystem_Stats_Authorized()
    {
        _fixture.Log("Act — GET /api/admin/system/stats with secret header");
        using var statusRequest = new HttpRequestMessage(HttpMethod.Get, "/api/admin/system/stats");
        statusRequest.Headers.Add("X-Admin-Secret", ApiFixture.AdminSecret);
        var statusResponse = await _fixture.Client.SendAsync(statusRequest);

        _fixture.LogAssertion("returns 200 OK with system status");
        statusResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await statusResponse.Content.ReadAsStringAsync();
        body.Should().Contain("environmentName");
        body.Should().Contain("objectCounts");
    }

    [Fact]
    public async Task AdminJournals_Authorized_ReturnsList()
    {
        _fixture.Log("Act — GET /api/admin/journal/entries with secret header");
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/journal/entries");
        request.Headers.Add("X-Admin-Secret", ApiFixture.AdminSecret);
        var response = await _fixture.Client.SendAsync(request);

        _fixture.LogAssertion("returns 200 OK");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AdminKnowledge_Domains_Authorized_ReturnsList()
    {
        _fixture.Log("Act — GET /api/admin/knowledge/domains with secret header");
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/knowledge/domains");
        request.Headers.Add("X-Admin-Secret", ApiFixture.AdminSecret);
        var response = await _fixture.Client.SendAsync(request);

        _fixture.LogAssertion("returns 200 OK");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
