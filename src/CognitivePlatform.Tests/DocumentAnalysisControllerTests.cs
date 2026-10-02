using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using CognitivePlatform.Api.Controllers;
using CognitivePlatform.Api.DocumentAnalysis;
using CognitivePlatform.Api.Integrations.Embeddings;
using CognitivePlatform.Api.Interpreter;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace CognitivePlatform.Tests;

public sealed class DocumentAnalysisControllerTests
{
    private const string Key = "test-document-analysis-credential-32-characters";

    [Fact]
    public async Task Endpoints_Reject_Missing_And_Rotated_Credentials_Before_Provider_Invocation()
    {
        var settings = Enabled();
        await using var host = await StartAsync(settings);
        using var client = Client(host);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/document-analysis/capabilities")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/document-analysis/insights", Request())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/document-analysis/embeddings", Request())).StatusCode);
        client.DefaultRequestHeaders.Add("X-Document-Analysis-Key", Key);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/document-analysis/capabilities")).StatusCode);
        settings.ClientKeySha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Key + "rotated")));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/document-analysis/capabilities")).StatusCode);
    }

    [Fact]
    public async Task Endpoints_Fail_Closed_When_Disabled_Or_Outside_Development()
    {
        foreach (var environment in new[] { "Development", "Production" })
        {
            var settings = Enabled();
            settings.Enabled = environment == "Production";
            await using var host = await StartAsync(settings, environment);
            using var client = Client(host);
            client.DefaultRequestHeaders.Add("X-Document-Analysis-Key", Key);

            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/api/document-analysis/capabilities")).StatusCode);
        }
    }

    [Fact]
    public async Task Embeddings_Validate_Hashes_And_Return_Ordered_Finite_Vectors()
    {
        var embeddings = new Mock<IEmbeddingService>(MockBehavior.Strict);
        embeddings.Setup(service => service.EmbedBatchAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new[] { new[] { 0.5f, 0.25f } });
        await using var host = await StartAsync(Enabled(), embeddings: embeddings.Object);
        using var client = Client(host);
        client.DefaultRequestHeaders.Add("X-Document-Analysis-Key", Key);
        var request = Request();

        var response = await client.PostAsJsonAsync("/api/document-analysis/embeddings", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("sourceHash", await response.Content.ReadAsStringAsync());
        var invalid = request with { Documents = new[] { request.Documents[0] with { SourceHash = "wrong" } } };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/document-analysis/embeddings", invalid)).StatusCode);
        embeddings.Verify(service => service.EmbedBatchAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Insights_Sanitize_Provider_Errors_And_Have_No_Domain_Dependencies()
    {
        var provider = new Mock<ILlmClient>(MockBehavior.Strict);
        provider.Setup(service => service.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new HttpRequestException("secret-source-text-and-key"));
        await using var host = await StartAsync(Enabled(), provider: provider.Object);
        using var client = Client(host);
        client.DefaultRequestHeaders.Add("X-Document-Analysis-Key", Key);

        var response = await client.PostAsJsonAsync("/api/document-analysis/insights", Request());
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.DoesNotContain("secret", await response.Content.ReadAsStringAsync());
        // This real HTTP host intentionally has no domain stores, orchestrator or action registry.
        provider.Verify(service => service.SendAsync(It.IsAny<string>(), "test-model", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Insights_Timeout_Keeps_Concurrency_Slot_Until_Uncooperative_Provider_Finishes()
    {
        var completion = new TaskCompletionSource<LlmResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new Mock<ILlmClient>(MockBehavior.Strict);
        provider.Setup(service => service.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(completion.Task);
        await using var host = await StartAsync(Enabled(), provider: provider.Object);
        using var client = Client(host);
        client.DefaultRequestHeaders.Add("X-Document-Analysis-Key", Key);

        Assert.Equal(HttpStatusCode.GatewayTimeout, (await client.PostAsJsonAsync("/api/document-analysis/insights", Request())).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostAsJsonAsync("/api/document-analysis/insights", Request())).StatusCode);
        completion.SetResult(new LlmResponse { Content = "derived answer" });
    }

    private static DocumentAnalysisRequest Request()
    {
        const string text = "unsaved reviewed source";
        return new(1, "request-1", new[] { new DocumentSnapshot("document-1", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))), text) });
    }

    private static DocumentAnalysisSettings Enabled() => new()
    {
        Enabled = true
      , ClientKeySha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Key)))
      , TimeoutSeconds = 1
    };

    private static HttpClient Client(WebApplication host)
        => new() { BaseAddress = new Uri(host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };

    private static async Task<WebApplication> StartAsync( DocumentAnalysisSettings settings
                                                       , string environment = "Development"
                                                       , IEmbeddingService? embeddings = null
                                                       , ILlmClient? provider = null )
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var monitor = new Mock<IOptionsMonitor<DocumentAnalysisSettings>>();
        monitor.SetupGet(options => options.CurrentValue).Returns(() => settings);
        var factory = new Mock<ILlmClientFactory>(MockBehavior.Strict);
        factory.SetupGet(service => service.DefaultProvider).Returns(LlmProvider.Ollama);
        factory.Setup(service => service.Create(LlmProvider.Ollama)).Returns(provider ?? Mock.Of<ILlmClient>());
        builder.Services.AddSingleton(monitor.Object);
        builder.Services.AddSingleton(Options.Create(new LlmClientSettings { DefaultModel = "test-model" }));
        builder.Services.AddSingleton(Options.Create(new EmbeddingSettings()));
        builder.Services.AddSingleton(factory.Object);
        builder.Services.AddSingleton(embeddings ?? Mock.Of<IEmbeddingService>());
        builder.Services.AddSingleton<DocumentAnalysisGate>();
        builder.Services.AddControllers().AddApplicationPart(typeof(DocumentAnalysisController).Assembly);
        var host = builder.Build();
        host.MapControllers();
        await host.StartAsync();
        return host;
    }
}
