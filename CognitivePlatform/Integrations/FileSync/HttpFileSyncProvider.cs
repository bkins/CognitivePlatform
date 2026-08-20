using System.Net.Http.Json;
using CognitivePlatform.Api.Integrations.FileSync.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CognitivePlatform.Api.Integrations.FileSync;

/// <summary>
/// IFileSyncProvider implementation that queries the LAA Android file gateway over HTTP.
/// Registered in DI when FileSync:GatewayBaseUrl is non-empty; falls back to
/// DisconnectedFileSyncProvider when the setting is absent.
/// </summary>
public sealed class HttpFileSyncProvider : IFileSyncProvider
{
    private const string ClientName  = "FileSync";
    private const string CpKeyHeader = "X-CP-Key";

    private readonly FileSyncSettings              _settings;
    private readonly IHttpClientFactory            _http;
    private readonly ILogger<HttpFileSyncProvider> _logger;

    public HttpFileSyncProvider( IOptions<FileSyncSettings>       settings
                               , IHttpClientFactory                http
                               , ILogger<HttpFileSyncProvider>     logger )
    {
        _settings = settings.Value;
        _http     = http;
        _logger   = logger;
    }

    public string DeviceName => _settings.DeviceName;

    // -----------------------------------------------------------------------
    // IsConnected — synchronous probe via GET /files/ping
    // -----------------------------------------------------------------------

    public bool IsConnected
    {
        get
        {
            if (_settings.GatewayBaseUrl.HasNoValue())
                return false;

            try
            {
                // Task.Run avoids a deadlock when called from a context that has a
                // SynchronizationContext (e.g. xUnit AsyncTestSyncContext).
                return Task.Run(PingCoreAsync).GetAwaiter().GetResult();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "IsConnected check failed unexpectedly");
                return false;
            }
        }
    }

    // -----------------------------------------------------------------------
    // PingAsync — public async probe, uses caller's CancellationToken
    // -----------------------------------------------------------------------

    public async Task<bool> PingAsync(CancellationToken ct = default)
    {
        try
        {
            using var client   = CreateClient();
            using var request  = BuildRequest(HttpMethod.Get, "/files/ping");
            var       response = await client.SendAsync(request, ct);
            return response.IsSuccessStatusCode;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "File sync ping failed");
            return false;
        }
    }

    // -----------------------------------------------------------------------
    // Data methods
    // -----------------------------------------------------------------------

    public async Task<IReadOnlyList<FileEntry>> ListFilesAsync(string remotePath, CancellationToken ct = default)
    {
        var url    = $"/files/list?path={Uri.EscapeDataString(remotePath)}";
        var result = await GetAsync<List<FileEntry>>(url, ct);
        return result ?? [];
    }

    public async Task<Stream> DownloadFileAsync(string remotePath, CancellationToken ct = default)
    {
        var url = $"/files/download?path={Uri.EscapeDataString(remotePath)}";

        using var client   = CreateClient();
        using var request  = BuildRequest(HttpMethod.Get, url);
        var       response = await client.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning( "File sync provider returned {Status} for {Url}: {Body}"
                              , response.StatusCode
                              , url
                              , body );
            throw new FileSyncProviderException( response.StatusCode
                                               , $"File sync provider returned {(int)response.StatusCode} for {url}" );
        }

        var memoryStream = new MemoryStream();
        await response.Content.CopyToAsync(memoryStream, ct);
        memoryStream.Position = 0;
        return memoryStream;
    }

    public async Task UploadFileAsync(string remotePath, Stream content, CancellationToken ct = default)
    {
        var url = $"/files/upload?path={Uri.EscapeDataString(remotePath)}";

        using var client  = CreateClient();
        using var request = BuildRequest(HttpMethod.Post, url);
        request.Content = new StreamContent(content);
        request.Content.Headers.ContentType
            = System.Net.Http.Headers.MediaTypeHeaderValue.Parse("application/octet-stream");

        var response = await client.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning( "File sync provider returned {Status} for {Url}: {Body}"
                              , response.StatusCode
                              , url
                              , body );
            throw new FileSyncProviderException( response.StatusCode
                                               , $"File sync provider returned {(int)response.StatusCode} for {url}" );
        }
    }

    public Task DeleteFileAsync(string remotePath, CancellationToken ct = default)
    {
        var url = $"/files/delete?path={Uri.EscapeDataString(remotePath)}";
        return SendAsync(HttpMethod.Delete, url, ct);
    }

    // -----------------------------------------------------------------------
    // Private helpers
    // -----------------------------------------------------------------------

    private async Task<bool> PingCoreAsync()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(_settings.PingTimeoutSeconds));

        try
        {
            using var client   = CreateClient();
            using var request  = BuildRequest(HttpMethod.Get, "/files/ping");
            var       response = await client.SendAsync(request, cts.Token);
            return response.IsSuccessStatusCode;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "File sync ping failed");
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
            _logger.LogWarning( "File sync provider returned {Status} for {Url}: {Body}"
                              , response.StatusCode
                              , relativeUrl
                              , body );
            throw new FileSyncProviderException( response.StatusCode
                                               , $"File sync provider returned {(int)response.StatusCode} for {relativeUrl}" );
        }

        var result = await response.Content.ReadFromJsonAsync<T>(ct);
        return result!;
    }

    private async Task SendAsync(HttpMethod method, string relativeUrl, CancellationToken ct)
    {
        using var client   = CreateClient();
        using var request  = BuildRequest(method, relativeUrl);
        var       response = await client.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning( "File sync provider returned {Status} for {Url}: {Body}"
                              , response.StatusCode
                              , relativeUrl
                              , body );
            throw new FileSyncProviderException( response.StatusCode
                                               , $"File sync provider returned {(int)response.StatusCode} for {relativeUrl}" );
        }
    }

    private HttpClient CreateClient()
    {
        var client = _http.CreateClient(ClientName);
        client.BaseAddress = new Uri(_settings.GatewayBaseUrl);
        return client;
    }

    private HttpRequestMessage BuildRequest(HttpMethod method, string relativeUrl)
    {
        var request = new HttpRequestMessage(method, relativeUrl);

        if (_settings.SharedSecret.HasValue())
            request.Headers.TryAddWithoutValidation(CpKeyHeader, _settings.SharedSecret);

        return request;
    }
}
