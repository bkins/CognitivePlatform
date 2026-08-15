using System.Net.Http.Headers;
using System.Net.Sockets;
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
    public static readonly string BaseUrl =
        Environment.GetEnvironmentVariable("API_BASE_URL") ?? "http://localhost:5276";
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

        AssertNotProduction();
    }

    private void AssertNotProduction()
    {
        try
        {
            var response = Client.GetAsync("/api/system/environment").GetAwaiter().GetResult();
            if (response.IsSuccessStatusCode)
            {
                var content = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;
                if (root.TryGetProperty("data", out var dataProp) &&
                    dataProp.TryGetProperty("Environment", out var envProp) &&
                    envProp.TryGetProperty("environmentName", out var nameProp))
                {
                    var envName = nameProp.GetString();
                    if (string.Equals(envName, "Prod", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(envName, "Production", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException(
                            $"SAFETY GUARD: Integration tests attempted to execute against '{envName}' environment ({BaseUrl})! Aborting immediately.");
                    }
                }
            }
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            // If the endpoint fails or is unreachable, IsApiOnline() will handle connection issues
        }
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

    /// <summary>
    /// Performs a fast TCP-level check to determine whether the API is accepting
    /// connections. Tests should call this at the start and return/skip if false
    /// rather than blocking until the 120 s HTTP timeout fires.
    /// </summary>
    public bool IsApiOnline()
    {
        var uri = new Uri(BaseUrl);
        try
        {
            using var tcp = new TcpClient();
            tcp.Connect(uri.Host, uri.Port);
            return true;
        }
        catch
        {
            return false;
        }
    }

    // ----------------------------------------------------------------
    // Private
    // ----------------------------------------------------------------

    private static HttpMessageHandler? BuildHandler(ITestOutputHelper? output)
        => IsVerbose && output is not null
               ? new VerboseLoggingHandler(output)
               : null;
}
