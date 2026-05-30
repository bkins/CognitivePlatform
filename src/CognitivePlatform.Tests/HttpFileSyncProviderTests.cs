using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using CognitivePlatform.Api.Integrations.FileSync;

namespace CognitivePlatform.Tests;

/// <summary>
/// Integration tests for HttpFileSyncProvider using a WireMock stub server.
/// Tests that require a timeout probe will take ~1 second to complete.
/// </summary>
public sealed class HttpFileSyncProviderTests : IDisposable
{
    private readonly WireMockServer     _server;
    private readonly IHttpClientFactory _httpFactory;

    public HttpFileSyncProviderTests()
    {
        _server = WireMockServer.Start();

        var services = new ServiceCollection();
        services.AddHttpClient("FileSync");
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
        _server.Given(Request.Create().WithPath("/files/ping").UsingGet())
               .RespondWith(Response.Create().WithStatusCode(200).WithBody("{\"status\":\"ok\"}"));

        var provider = BuildProvider();

        Assert.True(provider.IsConnected);
    }

    [Fact]
    public void IsConnected_ReturnsFalse_WhenGatewayBaseUrlIsEmpty()
    {
        var settings = Options.Create(new FileSyncSettings { GatewayBaseUrl = string.Empty });
        var provider = new HttpFileSyncProvider(settings, _httpFactory, NullLogger<HttpFileSyncProvider>.Instance);

        Assert.False(provider.IsConnected);
    }

    [Fact]
    public void IsConnected_ReturnsFalse_WhenServerIsUnreachable()
    {
        // Simulate unreachability by responding slower than the ping timeout.
        _server.Given(Request.Create().WithPath("/files/ping").UsingGet())
               .RespondWith(Response.Create()
                                    .WithStatusCode(200)
                                    .WithDelay(TimeSpan.FromSeconds(5)));

        var settings = Options.Create(new FileSyncSettings
                                      {
                                          GatewayBaseUrl     = _server.Url!
                                        , PingTimeoutSeconds = 1
                                      });
        var provider = new HttpFileSyncProvider(settings, _httpFactory, NullLogger<HttpFileSyncProvider>.Instance);

        Assert.False(provider.IsConnected);
    }

    [Fact]
    public void IsConnected_ReturnsFalse_WhenPingReturns503()
    {
        _server.Given(Request.Create().WithPath("/files/ping").UsingGet())
               .RespondWith(Response.Create().WithStatusCode(503));

        var provider = BuildProvider();

        Assert.False(provider.IsConnected);
    }

    // ================================================================
    // DeviceName
    // ================================================================

    [Fact]
    public void DeviceName_ReturnsConfiguredDeviceName()
    {
        var settings = Options.Create(new FileSyncSettings
                                      {
                                          GatewayBaseUrl = _server.Url!
                                        , DeviceName     = "Pixel 9"
                                      });
        var provider = new HttpFileSyncProvider(settings, _httpFactory, NullLogger<HttpFileSyncProvider>.Instance);

        Assert.Equal("Pixel 9", provider.DeviceName);
    }

    [Fact]
    public void DeviceName_ReturnsPhoneDefault_WhenNotConfigured()
    {
        var provider = BuildProvider();

        Assert.Equal("Phone", provider.DeviceName);
    }

    // ================================================================
    // ListFilesAsync
    // ================================================================

    [Fact]
    public async Task ListFilesAsync_DeserializesCorrectly_WhenServerReturns200()
    {
        _server.Given(Request.Create().WithPath("/files/list").UsingGet())
               .RespondWith(Response.Create()
                                    .WithStatusCode(200)
                                    .WithHeader("Content-Type", "application/json")
                                    .WithBody("""
                                              [
                                                {
                                                  "relativePath": "Documents/notes.txt",
                                                  "sizeBytes": 2048,
                                                  "lastModified": "2026-05-30T10:00:00+00:00",
                                                  "contentHash": null
                                                },
                                                {
                                                  "relativePath": "Documents/todo.md",
                                                  "sizeBytes": 512,
                                                  "lastModified": "2026-05-29T08:30:00+00:00",
                                                  "contentHash": "abc123"
                                                }
                                              ]
                                              """));

        var provider = BuildProvider();
        var result   = await provider.ListFilesAsync("/storage/emulated/0/Documents");

        Assert.Equal(2,                            result.Count);
        Assert.Equal("Documents/notes.txt",        result[0].RelativePath);
        Assert.Equal(2048,                         result[0].SizeBytes);
        Assert.Null(result[0].ContentHash);
        Assert.Equal("Documents/todo.md",          result[1].RelativePath);
        Assert.Equal(512,                          result[1].SizeBytes);
        Assert.Equal("abc123",                     result[1].ContentHash);
    }

    [Fact]
    public async Task ListFilesAsync_ReturnsEmptyList_WhenServerReturnsEmptyArray()
    {
        _server.Given(Request.Create().WithPath("/files/list").UsingGet())
               .RespondWith(Response.Create()
                                    .WithStatusCode(200)
                                    .WithHeader("Content-Type", "application/json")
                                    .WithBody("[]"));

        var provider = BuildProvider();
        var result   = await provider.ListFilesAsync("/storage/emulated/0/Documents");

        Assert.Empty(result);
    }

    [Fact]
    public async Task ListFilesAsync_ThrowsFileSyncProviderException_WhenServerReturnsNon200()
    {
        _server.Given(Request.Create().WithPath("/files/list").UsingGet())
               .RespondWith(Response.Create().WithStatusCode(403));

        var provider = BuildProvider();

        await Assert.ThrowsAsync<FileSyncProviderException>(
            () => provider.ListFilesAsync("/storage/emulated/0/Documents"));
    }

    [Fact]
    public async Task ListFilesAsync_SendsSharedSecretHeader_WhenSecretConfigured()
    {
        _server.Given(Request.Create()
                             .WithPath("/files/list")
                             .UsingGet()
                             .WithHeader("X-CP-Key", "test-secret"))
               .RespondWith(Response.Create()
                                    .WithStatusCode(200)
                                    .WithHeader("Content-Type", "application/json")
                                    .WithBody("[]"));

        var provider = BuildProvider(secret: "test-secret");
        var result   = await provider.ListFilesAsync("/storage/emulated/0/Documents");

        Assert.Empty(result);
    }

    // ================================================================
    // DownloadFileAsync
    // ================================================================

    [Fact]
    public async Task DownloadFileAsync_ReturnsStreamContent_WhenServerReturns200()
    {
        var fileContent = "Hello from the phone!"u8.ToArray();

        _server.Given(Request.Create().WithPath("/files/download").UsingGet())
               .RespondWith(Response.Create()
                                    .WithStatusCode(200)
                                    .WithHeader("Content-Type", "application/octet-stream")
                                    .WithBody(fileContent));

        var provider = BuildProvider();
        await using var stream = await provider.DownloadFileAsync("/storage/emulated/0/Documents/notes.txt");

        var buffer = new byte[fileContent.Length];
        var read   = await stream.ReadAsync(buffer);

        Assert.Equal(fileContent.Length, read);
        Assert.Equal(fileContent,        buffer);
    }

    [Fact]
    public async Task DownloadFileAsync_ThrowsFileSyncProviderException_WhenServerReturnsNon200()
    {
        _server.Given(Request.Create().WithPath("/files/download").UsingGet())
               .RespondWith(Response.Create().WithStatusCode(404));

        var provider = BuildProvider();

        await Assert.ThrowsAsync<FileSyncProviderException>(
            () => provider.DownloadFileAsync("/storage/emulated/0/Documents/missing.txt"));
    }

    // ================================================================
    // UploadFileAsync
    // ================================================================

    [Fact]
    public async Task UploadFileAsync_CompletesSuccessfully_WhenServerReturns200()
    {
        _server.Given(Request.Create().WithPath("/files/upload").UsingPost())
               .RespondWith(Response.Create().WithStatusCode(200));

        var provider = BuildProvider();
        var content  = new MemoryStream(Encoding.UTF8.GetBytes("file content"));

        await provider.UploadFileAsync("/storage/emulated/0/Documents/notes.txt", content);
    }

    [Fact]
    public async Task UploadFileAsync_ThrowsFileSyncProviderException_WhenServerReturnsNon200()
    {
        _server.Given(Request.Create().WithPath("/files/upload").UsingPost())
               .RespondWith(Response.Create().WithStatusCode(500));

        var provider = BuildProvider();
        var content  = new MemoryStream(Encoding.UTF8.GetBytes("file content"));

        await Assert.ThrowsAsync<FileSyncProviderException>(
            () => provider.UploadFileAsync("/storage/emulated/0/Documents/notes.txt", content));
    }

    // ================================================================
    // DeleteFileAsync
    // ================================================================

    [Fact]
    public async Task DeleteFileAsync_CompletesSuccessfully_WhenServerReturns200()
    {
        _server.Given(Request.Create().WithPath("/files/delete").UsingDelete())
               .RespondWith(Response.Create().WithStatusCode(200));

        var provider = BuildProvider();

        await provider.DeleteFileAsync("/storage/emulated/0/Documents/old.txt");
    }

    [Fact]
    public async Task DeleteFileAsync_ThrowsFileSyncProviderException_WhenServerReturnsNon200()
    {
        _server.Given(Request.Create().WithPath("/files/delete").UsingDelete())
               .RespondWith(Response.Create().WithStatusCode(404));

        var provider = BuildProvider();

        await Assert.ThrowsAsync<FileSyncProviderException>(
            () => provider.DeleteFileAsync("/storage/emulated/0/Documents/missing.txt"));
    }

    // ================================================================
    // PingAsync
    // ================================================================

    [Fact]
    public async Task PingAsync_ReturnsTrue_WhenServerReturns200()
    {
        _server.Given(Request.Create().WithPath("/files/ping").UsingGet())
               .RespondWith(Response.Create().WithStatusCode(200));

        var provider = BuildProvider();

        Assert.True(await provider.PingAsync());
    }

    [Fact]
    public async Task PingAsync_ReturnsFalse_WhenServerReturnsNon200()
    {
        _server.Given(Request.Create().WithPath("/files/ping").UsingGet())
               .RespondWith(Response.Create().WithStatusCode(503));

        var provider = BuildProvider();

        Assert.False(await provider.PingAsync());
    }

    // ================================================================
    // Private helpers
    // ================================================================

    private HttpFileSyncProvider BuildProvider(string? secret = null)
    {
        var settings = Options.Create(new FileSyncSettings
                                      {
                                          GatewayBaseUrl     = _server.Url!
                                        , SharedSecret       = secret ?? string.Empty
                                        , DeviceName         = "Phone"
                                        , PingTimeoutSeconds = 2
                                      });

        return new HttpFileSyncProvider(settings, _httpFactory, NullLogger<HttpFileSyncProvider>.Instance);
    }
}
