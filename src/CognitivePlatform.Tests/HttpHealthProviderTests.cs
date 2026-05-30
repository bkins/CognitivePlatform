using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using CognitivePlatform.Api.Integrations.Health;

namespace CognitivePlatform.Tests;

/// <summary>
/// Integration tests for HttpHealthProvider using a WireMock stub server.
/// Tests that require a timeout probe will take ~1 second to complete.
/// </summary>
public sealed class HttpHealthProviderTests : IDisposable
{
    private readonly WireMockServer     _server;
    private readonly IHttpClientFactory _httpFactory;

    public HttpHealthProviderTests()
    {
        _server = WireMockServer.Start();

        var services = new ServiceCollection();
        services.AddHttpClient("HealthConnect");
        _httpFactory = services.BuildServiceProvider()
                               .GetRequiredService<IHttpClientFactory>();
    }

    public void Dispose() => _server.Stop();

    // ================================================================
    // IsConnected
    // ================================================================

    [Fact]
    public void IsConnected_ReturnsTrue_WhenPingReturns200()
    {
        _server.Given(Request.Create().WithPath("/health/ping").UsingGet())
               .RespondWith(Response.Create().WithStatusCode(200).WithBody("{\"status\":\"ok\"}"));

        var provider = BuildProvider();

        Assert.True(provider.IsConnected);
    }

    [Fact]
    public void IsConnected_ReturnsFalse_WhenPhoneBaseUrlIsEmpty()
    {
        var settings = Options.Create(new HealthConnectSettings { PhoneBaseUrl = string.Empty });
        var provider = new HttpHealthProvider(settings, _httpFactory, NullLogger<HttpHealthProvider>.Instance);

        Assert.False(provider.IsConnected);
    }

    [Fact]
    public void IsConnected_ReturnsFalse_WhenServerIsUnreachable()
    {
        // Simulate unreachability by responding slower than the ping timeout.
        _server.Given(Request.Create().WithPath("/health/ping").UsingGet())
               .RespondWith(Response.Create()
                                    .WithStatusCode(200)
                                    .WithDelay(TimeSpan.FromSeconds(5)));

        var settings = Options.Create(new HealthConnectSettings
                                      {
                                          PhoneBaseUrl       = _server.Url!
                                        , PingTimeoutSeconds = 1
                                      });
        var provider = new HttpHealthProvider(settings, _httpFactory, NullLogger<HttpHealthProvider>.Instance);

        Assert.False(provider.IsConnected);
    }

    [Fact]
    public void IsConnected_ReturnsFalse_WhenPingReturns503()
    {
        _server.Given(Request.Create().WithPath("/health/ping").UsingGet())
               .RespondWith(Response.Create().WithStatusCode(503));

        var provider = BuildProvider();

        Assert.False(provider.IsConnected);
    }

    // ================================================================
    // GetStepCountAsync
    // ================================================================

    [Fact]
    public async Task GetStepCountAsync_DeserializesCorrectly_WhenServerReturns200()
    {
        _server.Given(Request.Create().WithPath("/health/steps").UsingGet())
               .RespondWith(Response.Create()
                                    .WithStatusCode(200)
                                    .WithHeader("Content-Type", "application/json")
                                    .WithBody("{\"steps\":8432,\"distanceMetres\":6100.5,\"activeCalories\":320}"));

        var provider = BuildProvider();
        var result   = await provider.GetStepCountAsync(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow);

        Assert.Equal(8432,   result.Steps);
        Assert.Equal(6100.5, result.DistanceMetres);
        Assert.Equal(320,    result.ActiveCalories);
    }

    [Fact]
    public async Task GetStepCountAsync_ThrowsHealthProviderException_WhenServerReturnsNon200()
    {
        _server.Given(Request.Create().WithPath("/health/steps").UsingGet())
               .RespondWith(Response.Create().WithStatusCode(500));

        var provider = BuildProvider();

        await Assert.ThrowsAsync<HealthProviderException>(
            () => provider.GetStepCountAsync(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task GetStepCountAsync_SendsSharedSecretHeader_WhenSecretConfigured()
    {
        _server.Given(Request.Create()
                             .WithPath("/health/steps")
                             .UsingGet()
                             .WithHeader("X-CP-Key", "test-secret"))
               .RespondWith(Response.Create()
                                    .WithStatusCode(200)
                                    .WithHeader("Content-Type", "application/json")
                                    .WithBody("{\"steps\":1000}"));

        var provider = BuildProvider(secret: "test-secret");
        var result   = await provider.GetStepCountAsync(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow);

        Assert.Equal(1000, result.Steps);
    }

    // ================================================================
    // GetSleepAsync
    // ================================================================

    [Fact]
    public async Task GetSleepAsync_DeserializesCorrectly_WhenServerReturns200()
    {
        _server.Given(Request.Create().WithPath("/health/sleep").UsingGet())
               .RespondWith(Response.Create()
                                    .WithStatusCode(200)
                                    .WithHeader("Content-Type", "application/json")
                                    .WithBody("{\"totalMinutes\":480,\"deepMinutes\":90,\"remMinutes\":120,\"lightMinutes\":270,\"sessions\":1}"));

        var provider = BuildProvider();
        var result   = await provider.GetSleepAsync(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow);

        Assert.Equal(480, result.TotalMinutes);
        Assert.Equal(90,  result.DeepMinutes);
        Assert.Equal(120, result.RemMinutes);
        Assert.Equal(1,   result.Sessions);
    }

    [Fact]
    public async Task GetSleepAsync_ThrowsHealthProviderException_WhenServerReturns404()
    {
        _server.Given(Request.Create().WithPath("/health/sleep").UsingGet())
               .RespondWith(Response.Create().WithStatusCode(404));

        var provider = BuildProvider();

        await Assert.ThrowsAsync<HealthProviderException>(
            () => provider.GetSleepAsync(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow));
    }

    // ================================================================
    // GetHeartRateAsync
    // ================================================================

    [Fact]
    public async Task GetHeartRateAsync_DeserializesCorrectly_WhenServerReturns200()
    {
        _server.Given(Request.Create().WithPath("/health/heart-rate").UsingGet())
               .RespondWith(Response.Create()
                                    .WithStatusCode(200)
                                    .WithHeader("Content-Type", "application/json")
                                    .WithBody("{\"averageBpm\":72,\"minBpm\":55,\"maxBpm\":145,\"restingBpm\":58}"));

        var provider = BuildProvider();
        var result   = await provider.GetHeartRateAsync(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow);

        Assert.Equal(72,  result.AverageBpm);
        Assert.Equal(55,  result.MinBpm);
        Assert.Equal(145, result.MaxBpm);
        Assert.Equal(58,  result.RestingBpm);
    }

    [Fact]
    public async Task GetHeartRateAsync_ThrowsHealthProviderException_WhenServerReturnsNon200()
    {
        _server.Given(Request.Create().WithPath("/health/heart-rate").UsingGet())
               .RespondWith(Response.Create().WithStatusCode(403));

        var provider = BuildProvider();

        await Assert.ThrowsAsync<HealthProviderException>(
            () => provider.GetHeartRateAsync(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow));
    }

    // ================================================================
    // GetDistanceAsync
    // ================================================================

    [Fact]
    public async Task GetDistanceAsync_DeserializesCorrectly_WhenServerReturns200()
    {
        _server.Given(Request.Create().WithPath("/health/distance").UsingGet())
               .RespondWith(Response.Create()
                                    .WithStatusCode(200)
                                    .WithHeader("Content-Type", "application/json")
                                    .WithBody("{\"metres\":8000.0,\"steps\":9800,\"activeCalories\":420}"));

        var provider = BuildProvider();
        var result   = await provider.GetDistanceAsync(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow);

        Assert.Equal(8000.0, result.Metres);
        Assert.Equal(9800,   result.Steps);
        Assert.Equal(420,    result.ActiveCalories);
    }

    [Fact]
    public async Task GetDistanceAsync_ThrowsHealthProviderException_WhenServerReturnsNon200()
    {
        _server.Given(Request.Create().WithPath("/health/distance").UsingGet())
               .RespondWith(Response.Create().WithStatusCode(500));

        var provider = BuildProvider();

        await Assert.ThrowsAsync<HealthProviderException>(
            () => provider.GetDistanceAsync(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow));
    }

    // ================================================================
    // Private helpers
    // ================================================================

    private HttpHealthProvider BuildProvider(string? secret = null)
    {
        var settings = Options.Create(new HealthConnectSettings
                                      {
                                          PhoneBaseUrl       = _server.Url!
                                        , SharedSecret       = secret ?? string.Empty
                                        , PingTimeoutSeconds = 2
                                      });

        return new HttpHealthProvider(settings, _httpFactory, NullLogger<HttpHealthProvider>.Instance);
    }
}
