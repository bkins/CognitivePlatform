using System.Net;
using CognitivePlatform.Api.Interpreter;
using Microsoft.Extensions.Options;

namespace CognitivePlatform.Tests;

public class GeminiLlmClientTests
{
    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task SendAsync_ThrowsHttpRequestException_WhenProviderIsUnavailable(HttpStatusCode statusCode)
    {
        using var httpClient = new HttpClient(new StaticResponseHandler(statusCode));
        var settings = Options.Create(new LlmClientSettings
                                      {
                                          Timeout = 30
                                        , Gemini  = new GeminiSettings
                                                     {
                                                         Endpoint = "http://localhost"
                                                       , Model    = "gemini-test"
                                                     }
                                      });
        var client = new GeminiLlmClient(httpClient, settings);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => client.SendAsync("safe test prompt"));

        Assert.Equal(statusCode, exception.StatusCode);
    }

    private sealed class StaticResponseHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;

        public StaticResponseHandler(HttpStatusCode statusCode)
        {
            _statusCode = statusCode;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(_statusCode)
                                   {
                                       Content = new StringContent("{\"error\":\"provider unavailable\"}")
                                   });
        }
    }
}
