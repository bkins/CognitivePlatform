using System.Net;
using CognitivePlatform.Api.DocumentAnalysis;
using Moq;

namespace CognitivePlatform.Tests;

public sealed class DocumentModelRevisionResolverTests
{
    [Theory]
    [InlineData("model", "model:latest", true)]
    [InlineData("model:v2", "model:v2", true)]
    [InlineData("model", "other:latest", false)]
    public async Task ResolveAsync_Uses_Exact_Catalog_Model_And_Valid_Digest(string requested, string present, bool matches)
    {
        var digest = new string('a', 64);
        using var handler = new CatalogHandler($"{{\"models\":[{{\"name\":\"{present}\",\"digest\":\"{digest}\"}}]}}");
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(service => service.CreateClient("Ollama")).Returns(() => new HttpClient(handler, false));
        var resolver = new DocumentModelRevisionResolver(factory.Object);

        var actual = await resolver.ResolveAsync("http://localhost:11434", requested, default);

        Assert.Equal(matches ? digest : null, actual);
        Assert.Equal(HttpMethod.Get, handler.Method);
        Assert.False(handler.HadContentOrAnalysisCredential);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"models\":[{\"name\":\"model:latest\",\"digest\":\"invalid\"}]}")]
    public async Task ResolveAsync_Malformed_Or_Unknown_Digest_Does_Not_Claim_Model_Identity(string body)
    {
        using var handler = new CatalogHandler(body);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(service => service.CreateClient("Ollama")).Returns(() => new HttpClient(handler, false));

        Assert.Null(await new DocumentModelRevisionResolver(factory.Object).ResolveAsync("http://localhost:11434", "model", default));
    }

    private sealed class CatalogHandler(string body) : HttpMessageHandler
    {
        internal HttpMethod? Method { get; private set; }
        internal bool HadContentOrAnalysisCredential { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Method = request.Method;
            HadContentOrAnalysisCredential = request.Content is not null || request.Headers.Contains("X-Document-Analysis-Key");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }
}
