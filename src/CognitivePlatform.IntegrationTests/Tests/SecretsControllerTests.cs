using System.Net;
using System.Net.Http.Json;
using CognitivePlatform.Api.Controllers;
using CognitivePlatform.IntegrationTests.Infrastructure;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace CognitivePlatform.IntegrationTests.Tests;

[Trait("Category", "Integration")]
public sealed class SecretsControllerTests : IDisposable
{
    private readonly ApiFixture _fixture;

    public SecretsControllerTests(ITestOutputHelper output)
    {
        _fixture = new ApiFixture(output);
    }

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task Status_ReturnsVaultStatus()
    {
        _fixture.Log("Act — GET /api/secrets/status");
        var response = await _fixture.Client.GetAsync("/api/secrets/status");

        _fixture.LogAssertion("returns 200 OK with initialization and unlock flags");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("isInitialized");
        body.Should().Contain("isUnlocked");
    }

    [Fact]
    public async Task Setup_Unlock_Lock_Lifecycle_RoundTrip()
    {
        _fixture.Log("Act — POST /api/secrets/setup with new PIN");
        var pinPayload = new VaultPinRequest("123456");
        var setupResponse = await _fixture.Client.PostAsJsonAsync("/api/secrets/setup", pinPayload);

        // If already initialized in previous run, setup returns BadRequest; in either case test unlock/lock
        if (setupResponse.StatusCode == HttpStatusCode.OK)
        {
            _fixture.LogAssertion("setup returned 200 OK");
            var setupBody = await setupResponse.Content.ReadAsStringAsync();
            setupBody.Should().Contain("initialized");
        }

        _fixture.Log("Act — POST /api/secrets/unlock with PIN");
        var unlockResponse = await _fixture.Client.PostAsJsonAsync("/api/secrets/unlock", pinPayload);
        unlockResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var statusAfterUnlock = await _fixture.Client.GetAsync("/api/secrets/status");
        var unlockBody = await statusAfterUnlock.Content.ReadAsStringAsync();
        unlockBody.Should().Contain("\"isUnlocked\":true");

        _fixture.Log("Act — POST /api/secrets/lock");
        var lockResponse = await _fixture.Client.PostAsync("/api/secrets/lock", null);
        lockResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var statusAfterLock = await _fixture.Client.GetAsync("/api/secrets/status");
        var lockBody = await statusAfterLock.Content.ReadAsStringAsync();
        lockBody.Should().Contain("\"isUnlocked\":false");
    }
}
