using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CognitivePlatform.Api.Interpreter;
using Microsoft.Extensions.Options;
using Moq;

namespace CognitivePlatform.Tests;

/// <summary>
/// Verifies that GroqLlmClient correctly captures rate-limit headers and usage body
/// into the returned LlmResponse. Uses a fake HttpMessageHandler to avoid real HTTP calls.
/// </summary>
public class GroqLlmClientTests
{
    private static GroqLlmClient BuildClient( HttpMessageHandler       handler
                                            , IGroqUsageTracker?       tracker     = null
                                            , ILlmRateLimiter?         rateLimiter = null )
    {
        var httpClient = new HttpClient(handler);

        var settings = new LlmClientSettings
                       {
                               Provider     = LlmProvider.Groq
                             , DefaultModel = "llama-3.3-70b-versatile"
                             , Timeout      = 30
                             , Groq         = new GroqSettings
                                             {
                                                     Endpoint = "https://api.groq.com/openai/v1"
                                                   , Model    = "llama-3.3-70b-versatile"
                                                   , ApiKey   = "test-key"
                                             }
                       };

        tracker     ??= new Mock<IGroqUsageTracker>().Object;
        rateLimiter ??= new Mock<ILlmRateLimiter>().Object;

        return new GroqLlmClient(httpClient
                               , Options.Create(settings)
                               , tracker
                               , rateLimiter);
    }

    private static HttpMessageHandler SuccessHandlerWithHeaders( string  responseContent
                                                                , IDictionary<string, string>? extraHeaders = null )
    {
        return new FakeHttpHandler(HttpStatusCode.OK
                                 , responseContent
                                 , extraHeaders ?? new Dictionary<string, string>());
    }

    private static string ChatResponseJson(int promptTokens, int completionTokens, int totalTokens)
    {
        return JsonSerializer.Serialize(new
               {
                       choices = new[]
                                 {
                                     new { message = new { role = "assistant", content = "hello" } }
                                 }
                     , usage   = new
                                 {
                                         prompt_tokens     = promptTokens
                                       , completion_tokens = completionTokens
                                       , total_tokens      = totalTokens
                                 }
               });
    }

    // ----------------------------------------------------------------
    // Usage body
    // ----------------------------------------------------------------

    [Fact]
    public async Task SendAsync_PopulatesUsage_FromResponseBody()
    {
        var json    = ChatResponseJson(promptTokens: 50, completionTokens: 25, totalTokens: 75);
        var handler = SuccessHandlerWithHeaders(json);
        var client  = BuildClient(handler);

        var result = await client.SendAsync("test prompt");

        Assert.Equal(50, result.Usage.PromptTokens);
        Assert.Equal(25, result.Usage.CompletionTokens);
        Assert.Equal(75, result.Usage.TotalTokens);
    }

    // ----------------------------------------------------------------
    // Rate-limit headers present
    // ----------------------------------------------------------------

    [Fact]
    public async Task SendAsync_PopulatesRateLimits_WhenHeadersArePresent()
    {
        var headers = new Dictionary<string, string>
                      {
                              ["x-ratelimit-limit-requests"]     = "100"
                            , ["x-ratelimit-remaining-requests"] = "80"
                            , ["x-ratelimit-reset-requests"]     = "30s"
                            , ["x-ratelimit-limit-tokens"]       = "10000"
                            , ["x-ratelimit-remaining-tokens"]   = "9000"
                            , ["x-ratelimit-reset-tokens"]       = "1m"
                      };

        var json    = ChatResponseJson(10, 5, 15);
        var handler = SuccessHandlerWithHeaders(json, headers);
        var client  = BuildClient(handler);

        var result = await client.SendAsync("test prompt");

        Assert.True(result.RateLimits.HasData);
        Assert.Equal(100, result.RateLimits.RequestLimit);
        Assert.Equal(80,  result.RateLimits.RequestsRemaining);
        Assert.Equal("30s", result.RateLimits.RequestsResetRaw);
        Assert.Equal(10000, result.RateLimits.TokenLimit);
        Assert.Equal(9000,  result.RateLimits.TokensRemaining);
        Assert.Equal("Groq", result.RateLimits.Provider);
    }

    // ----------------------------------------------------------------
    // Rate-limit headers absent
    // ----------------------------------------------------------------

    [Fact]
    public async Task SendAsync_RateLimitsHasNoData_WhenHeadersAreAbsent()
    {
        var json    = ChatResponseJson(10, 5, 15);
        var handler = SuccessHandlerWithHeaders(json);
        var client  = BuildClient(handler);

        var result = await client.SendAsync("test prompt");

        Assert.False(result.RateLimits.HasData);
        Assert.Equal(0, result.RateLimits.RequestLimit);
        Assert.Equal(0, result.RateLimits.TokenLimit);
    }

    // ----------------------------------------------------------------
    // Fake HttpMessageHandler
    // ----------------------------------------------------------------

    private sealed class FakeHttpHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode              _statusCode;
        private readonly string                      _content;
        private readonly IDictionary<string, string> _responseHeaders;

        public FakeHttpHandler( HttpStatusCode              statusCode
                              , string                      content
                              , IDictionary<string, string> responseHeaders )
        {
            _statusCode      = statusCode;
            _content         = content;
            _responseHeaders = responseHeaders;
        }

        protected override Task<HttpResponseMessage> SendAsync( HttpRequestMessage  request
                                                              , CancellationToken   cancellationToken )
        {
            var response = new HttpResponseMessage(_statusCode)
                           {
                               Content = new StringContent(_content, Encoding.UTF8, "application/json")
                           };

            foreach (var (key, value) in _responseHeaders)
                response.Headers.TryAddWithoutValidation(key, value);

            return Task.FromResult(response);
        }
    }
}
