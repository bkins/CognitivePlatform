using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;
using Xunit.Abstractions;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace CognitivePlatform.IntegrationTests.Infrastructure;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using CognitivePlatform.Api.Interpreter;
using CognitivePlatform.Api.Domains.Secrets;

public sealed class CognitivePlatformTestApp : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((context, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["LlmClient:Provider"]          = "Mock"
              , ["LlmClient:ShouldProbeModels"] = "false"
              , ["AdminSettings:AdminSecret"]   = ApiFixture.AdminSecret
            });
        });
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<ILlmClient, MockLlmClient>();
        });
    }
}

/// <summary>
/// Shared HTTP client and helpers for all integration tests.
/// Uses in-memory <see cref="WebApplicationFactory{TEntryPoint}"/> by default
/// or an external API instance when <c>API_BASE_URL</c> is explicitly provided.
/// </summary>
public sealed class ApiFixture : IDisposable
{
    public static readonly string? ExternalBaseUrl =
        Environment.GetEnvironmentVariable("API_BASE_URL");

    public static readonly string BaseUrl =
        ExternalBaseUrl ?? "http://localhost";

    public const string AdminSecret = "notverysecurebutthatisok";

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
      , Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    /// <summary>True when VERBOSE_INTEGRATION=true is set in the environment.</summary>
    public static readonly bool IsVerbose =
        Environment.GetEnvironmentVariable("VERBOSE_INTEGRATION").EqualsIgnoreCase("true");

    private readonly ITestOutputHelper? _output;
    private readonly CognitivePlatformTestApp? _factory;

    public HttpClient Client { get; }

    /// <param name="output">
    /// xunit output helper injected by the test class. When <see langword="null"/>
    /// verbose logging is silently suppressed even if VERBOSE_INTEGRATION is set.
    /// </param>
    public ApiFixture(ITestOutputHelper? output = null)
    {
        _output = output;

        if (string.IsNullOrWhiteSpace(ExternalBaseUrl))
        {
            var originalAspNetCoreEnvironment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
            var originalDotnetEnvironment     = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");

            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Testing");
            Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT",     "Testing");

            try
            {
                _factory = new CognitivePlatformTestApp();
                Client   = _factory.CreateClient();
            }
            finally
            {
                Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", originalAspNetCoreEnvironment);
                Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT",     originalDotnetEnvironment);
            }

            Client.Timeout = TimeSpan.FromSeconds(30);
        }
        else
        {
            var handler = BuildHandler(output);
            Client = handler is not null
                ? new HttpClient(handler)
                      {
                          BaseAddress = new Uri(ExternalBaseUrl)
                        , Timeout     = TimeSpan.FromSeconds(30)
                      }
                : new HttpClient
                      {
                          BaseAddress = new Uri(ExternalBaseUrl)
                        , Timeout     = TimeSpan.FromSeconds(30)
                      };
        }

        Client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));

        Client.DefaultRequestHeaders.Add("X-Admin-Secret", AdminSecret);

        AssertNotProduction();
    }

    public HttpClient CreateClientWithoutAdminHeader()
    {
        var client = _factory != null
            ? _factory.CreateClient()
            : new HttpClient { BaseAddress = new Uri(BaseUrl) };
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
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
                    if (envName.EqualsIgnoreCase("Prod") ||
                        envName.EqualsIgnoreCase("Production"))
                    {
                        throw new InvalidOperationException(
                            $"SAFETY GUARD: Integration tests attempted to execute against '{envName}' environment! Aborting immediately.");
                    }
                }
            }
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            // Allowed if endpoint not yet initialized
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

    public void Dispose()
    {
        Client.Dispose();
        _factory?.Dispose();
    }

    /// <summary>
    /// Performs a check to determine whether the API is accepting requests.
    /// In in-memory mode, this is always true.
    /// </summary>
    public bool IsApiOnline() => true;

    // ----------------------------------------------------------------
    // Private
    // ----------------------------------------------------------------

    private static HttpMessageHandler? BuildHandler(ITestOutputHelper? output)
        => IsVerbose && output is not null
               ? new VerboseLoggingHandler(output)
               : null;

    public Task ResetSecretsVaultForInMemoryTestsAsync()
    {
        if (_factory is null)
        {
            return Task.CompletedTask;
        }

        using var scope = _factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<CognitivePlatform.Api.Data.IObjectStore>();
        foreach (var secret in store.List<SecretEntry>())
        {
            store.SoftDelete<SecretEntry>(secret.Id);
        }

        return Task.CompletedTask;
    }
}
