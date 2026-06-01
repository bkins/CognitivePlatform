using System.Text.Json;
using CognitivePlatform.Api.Integrations.Embeddings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace CognitivePlatform.Tests;

/// <summary>
/// Tests for OllamaEmbeddingService using a WireMock stub server.
/// The "unreachable server" tests connect to a port that is not listening
/// and will return false quickly on connection refused.
/// </summary>
public sealed class OllamaEmbeddingServiceTests : IDisposable
{
    private readonly WireMockServer     _server;
    private readonly IHttpClientFactory _httpFactory;

    public OllamaEmbeddingServiceTests()
    {
        _server = WireMockServer.Start();

        var services = new ServiceCollection();
        services.AddHttpClient("OllamaEmbedding");
        _httpFactory = services.BuildServiceProvider()
                               .GetRequiredService<IHttpClientFactory>();
    }

    public void Dispose() => _server.Stop();

    // ================================================================
    // IsAvailable
    // ================================================================

    [Fact]
    public void IsAvailable_ReturnsTrue_WhenOllamaResponds()
    {
        _server.Given(Request.Create().WithPath("/api/tags").UsingGet())
               .RespondWith(Response.Create().WithStatusCode(200).WithBody("{\"models\":[]}"));

        var service = BuildService();

        Assert.True(service.IsAvailable);
    }

    [Fact]
    public void IsAvailable_ReturnsFalse_WhenServerIsUnreachable()
    {
        var service = BuildService(baseUrl: "http://127.0.0.1:19999", pingTimeoutSeconds: 1);

        Assert.False(service.IsAvailable);
    }

    [Fact]
    public void IsAvailable_ReturnsFalse_WhenServerReturnsError()
    {
        _server.Given(Request.Create().WithPath("/api/tags").UsingGet())
               .RespondWith(Response.Create().WithStatusCode(503));

        var service = BuildService();

        Assert.False(service.IsAvailable);
    }

    // ================================================================
    // EmbedAsync
    // ================================================================

    [Fact]
    public async Task EmbedAsync_ReturnsEmbedding_WhenServerRespondsSuccessfully()
    {
        var responseJson = JsonSerializer.Serialize(new { embedding = new float[] { 0.1f, 0.2f, 0.3f } });

        _server.Given(Request.Create().WithPath("/api/embeddings").UsingPost())
               .RespondWith(Response.Create()
                                    .WithStatusCode(200)
                                    .WithBody(responseJson)
                                    .WithHeader("Content-Type", "application/json"));

        var service = BuildService();

        var result = await service.EmbedAsync("hello world");

        Assert.NotEmpty(result);
        Assert.Equal(3, result.Length);
    }

    [Fact]
    public async Task EmbedAsync_ReturnsEmpty_WhenServerReturnsError()
    {
        _server.Given(Request.Create().WithPath("/api/embeddings").UsingPost())
               .RespondWith(Response.Create().WithStatusCode(500));

        var service = BuildService();

        var result = await service.EmbedAsync("hello world");

        Assert.Empty(result);
    }

    [Fact]
    public async Task EmbedAsync_ReturnsEmpty_WhenTextIsWhiteSpace()
    {
        var service = BuildService();

        var result = await service.EmbedAsync("   ");

        Assert.Empty(result);
    }

    [Fact]
    public async Task EmbedAsync_ReturnsEmpty_WhenServerIsUnreachable()
    {
        var service = BuildService(baseUrl: "http://127.0.0.1:19999");

        var result = await service.EmbedAsync("hello");

        Assert.Empty(result);
    }

    // ================================================================
    // EmbedBatchAsync
    // ================================================================

    [Fact]
    public async Task EmbedBatchAsync_ReturnsOneResultPerInput()
    {
        var responseJson = JsonSerializer.Serialize(new { embedding = new float[] { 0.1f, 0.2f } });

        _server.Given(Request.Create().WithPath("/api/embeddings").UsingPost())
               .RespondWith(Response.Create()
                                    .WithStatusCode(200)
                                    .WithBody(responseJson)
                                    .WithHeader("Content-Type", "application/json"));

        var service = BuildService();

        var result = await service.EmbedBatchAsync(new[] { "first", "second", "third" });

        Assert.Equal(3, result.Length);
        Assert.All(result, embedding => Assert.Equal(2, embedding.Length));
    }

    // ================================================================
    // Helpers
    // ================================================================

    private OllamaEmbeddingService BuildService(string? baseUrl = null, int pingTimeoutSeconds = 4)
    {
        var settings = Options.Create(new EmbeddingSettings
                                      {
                                          OllamaBaseUrl      = baseUrl ?? _server.Url!
                                        , EmbeddingModel     = "nomic-embed-text"
                                        , PingTimeoutSeconds = pingTimeoutSeconds
                                      });

        return new OllamaEmbeddingService( settings
                                         , _httpFactory
                                         , NullLogger<OllamaEmbeddingService>.Instance );
    }
}
