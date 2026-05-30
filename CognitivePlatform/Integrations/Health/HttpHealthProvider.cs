using System.Net.Http.Json;
using CognitivePlatform.Api.Integrations.Health.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CognitivePlatform.Api.Integrations.Health;

/// <summary>
/// IHealthProvider implementation that queries the LAA Android app over HTTP.
/// Registered in DI when HealthConnect:PhoneBaseUrl is non-empty; falls back
/// to DisconnectedHealthProvider when the setting is absent.
/// </summary>
public sealed class HttpHealthProvider : IHealthProvider
{
    private const string ClientName  = "HealthConnect";
    private const string CpKeyHeader = "X-CP-Key";

    private readonly HealthConnectSettings        _settings;
    private readonly IHttpClientFactory           _http;
    private readonly ILogger<HttpHealthProvider>  _logger;

    public HttpHealthProvider( IOptions<HealthConnectSettings>  settings
                             , IHttpClientFactory                http
                             , ILogger<HttpHealthProvider>       logger )
    {
        _settings = settings.Value;
        _http     = http;
        _logger   = logger;
    }

    // -----------------------------------------------------------------------
    // IsConnected — synchronous probe via GET /health/ping
    // -----------------------------------------------------------------------

    public bool IsConnected
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_settings.PhoneBaseUrl))
                return false;

            try
            {
                // Task.Run avoids a deadlock when called from a context that has a
                // SynchronizationContext (e.g. xUnit AsyncTestSyncContext).
                return Task.Run(PingAsync).GetAwaiter().GetResult();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "IsConnected check failed unexpectedly");
                return false;
            }
        }
    }

    // -----------------------------------------------------------------------
    // Data methods
    // -----------------------------------------------------------------------

    public Task<StepCountResult> GetStepCountAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var url = $"/health/steps?from={Encode(from)}&to={Encode(to)}";
        return GetAsync<StepCountResult>(url, ct);
    }

    public Task<SleepResult> GetSleepAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var url = $"/health/sleep?from={Encode(from)}&to={Encode(to)}";
        return GetAsync<SleepResult>(url, ct);
    }

    public Task<HeartRateResult> GetHeartRateAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var url = $"/health/heart-rate?from={Encode(from)}&to={Encode(to)}";
        return GetAsync<HeartRateResult>(url, ct);
    }

    public Task<DistanceResult> GetDistanceAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var url = $"/health/distance?from={Encode(from)}&to={Encode(to)}";
        return GetAsync<DistanceResult>(url, ct);
    }

    // -----------------------------------------------------------------------
    // Private helpers
    // -----------------------------------------------------------------------

    private async Task<bool> PingAsync()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(_settings.PingTimeoutSeconds));

        try
        {
            using var client   = CreateClient();
            using var request  = BuildRequest(HttpMethod.Get, "/health/ping");
            var       response = await client.SendAsync(request, cts.Token);
            return response.IsSuccessStatusCode;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Health ping failed");
            return false;
        }
    }

    private async Task<T> GetAsync<T>(string relativeUrl, CancellationToken ct)
    {
        using var client   = CreateClient();
        using var request  = BuildRequest(HttpMethod.Get, relativeUrl);
        var       response = await client.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning( "Health provider returned {Status} for {Url}: {Body}"
                              , response.StatusCode
                              , relativeUrl
                              , body );
            throw new HealthProviderException( response.StatusCode
                                             , $"Health provider returned {(int)response.StatusCode} for {relativeUrl}" );
        }

        var result = await response.Content.ReadFromJsonAsync<T>(ct);
        return result!;
    }

    private HttpClient CreateClient()
    {
        var client = _http.CreateClient(ClientName);
        client.BaseAddress = new Uri(_settings.PhoneBaseUrl);
        return client;
    }

    private HttpRequestMessage BuildRequest(HttpMethod method, string relativeUrl)
    {
        var request = new HttpRequestMessage(method, relativeUrl);

        if (!string.IsNullOrWhiteSpace(_settings.SharedSecret))
            request.Headers.TryAddWithoutValidation(CpKeyHeader, _settings.SharedSecret);

        return request;
    }

    private static string Encode(DateTimeOffset value)
        => Uri.EscapeDataString(value.ToString("O"));
}
