using System.Net.Http.Headers;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace CognitivePlatform.IntegrationTests.Infrastructure;

/// <summary>
/// Shared HTTP client and helpers for all integration tests.
/// Creates a single <see cref="HttpClient"/> pointed at the Dev API
/// and provides convenience methods for common operations.
///
/// Verbose logging is activated by setting the VERBOSE_INTEGRATION
/// environment variable to "true" (case-insensitive).  When active,
/// every HTTP request/response is written to the xunit output alongside
/// step and assertion markers emitted by the tests themselves.
///
/// Usage:
///   VERBOSE_INTEGRATION=true dotnet test --filter "Category=Integration" -v detailed
/// </summary>
public sealed class ApiFixture : IDisposable
{
    public const string BaseUrl     = "http://localhost:5273";
    public const string AdminSecret = "notverysecurebutthatisok";

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>True when VERBOSE_INTEGRATION=true is set in the environment.</summary>
    public static readonly bool IsVerbose =
        string.Equals(
            Environment.GetEnvironmentVariable("VERBOSE_INTEGRATION")
          , "true"
          , StringComparison.OrdinalIgnoreCase);

    private readonly ITestOutputHelper? _output;

    public HttpClient Client { get; }

    /// <param name="output">
    /// xunit output helper injected by the test class.  When <see langword="null"/>
    /// verbose logging is silently suppressed even if VERBOSE_INTEGRATION is set.
    /// </param>
    public ApiFixture(ITestOutputHelper? output = null)
    {
        _output = output;

        var handler = BuildHandler(output);

        Client = handler is not null
            ? new HttpClient(handler)
                  {
                      BaseAddress = new Uri(BaseUrl)
                    , Timeout     = TimeSpan.FromSeconds(120)
                  }
            : new HttpClient
                  {
                      BaseAddress = new Uri(BaseUrl)
                    , Timeout     = TimeSpan.FromSeconds(120)
                  };

        Client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));

        Client.DefaultRequestHeaders.Add("X-Admin-Secret", AdminSecret);
    }

    // ----------------------------------------------------------------
    // Verbose helpers
    // ----------------------------------------------------------------

    /// <summary>
    /// Emits a named step to the xunit output when verbose mode is active.
    /// Use at the start of each logical phase inside a test (Arrange / Act / Assert).
    /// </summary>
    public void Log(string message)
    {
        if (IsVerbose)
            _output?.WriteLine($"  ▶ {message}");
    }

    /// <summary>
    /// Emits the assertion description to the xunit output when verbose mode is active.
    /// Call immediately before a <c>.Should()…</c> chain to identify which check failed.
    /// </summary>
    public void LogAssertion(string description)
    {
        if (IsVerbose)
            _output?.WriteLine($"  ✔ Asserting: {description}");
    }

    // ----------------------------------------------------------------
    // HTTP helpers
    // ----------------------------------------------------------------

    /// <summary>
    /// Deserializes a successful JSON response body into <typeparamref name="T"/>.
    /// Throws <see cref="InvalidOperationException"/> if the response was not successful.
    /// The raw body is already logged by <see cref="VerboseLoggingHandler"/> when
    /// verbose mode is active; no duplicate logging is performed here.
    /// </summary>
    public async Task<T> ReadJsonAsync<T>(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Expected success but got {(int)response.StatusCode}: {body}");

        return JsonSerializer.Deserialize<T>(body, JsonOptions)
               ?? throw new InvalidOperationException($"Deserialized null from: {body}");
    }

    public void Dispose() => Client.Dispose();

    // ----------------------------------------------------------------
    // Private
    // ----------------------------------------------------------------

    private static HttpMessageHandler? BuildHandler(ITestOutputHelper? output)
        => IsVerbose && output is not null
               ? new VerboseLoggingHandler(output)
               : null;
}
