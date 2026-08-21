using Microsoft.Extensions.Options;

namespace CognitivePlatform.Api.Interpreter;

// ── Settings ─────────────────────────────────────────────────────────────────

/// <summary>
/// Bound from "LlmFallback" section in appsettings.json.
/// Defines the ordered chain of providers/models tried on a 429 or rate-limit
/// response from the primary provider.
/// </summary>
public sealed class LlmFallbackSettings
{
    public bool                   Enabled      { get; set; }
    public string                 FallbackNote { get; set; } = "Response generated via fallback model — primary provider rate-limited.";
    public List<LlmFallbackEntry> Chain        { get; set; } = [];
}

/// <summary>
/// A single entry in the fallback chain. When <see cref="HealthCheckUrl"/> is
/// set the endpoint is probed before including this entry in the viable list.
/// </summary>
public sealed class LlmFallbackEntry
{
    public string  Provider                  { get; set; } = string.Empty;
    public string  Model                     { get; set; } = string.Empty;
    /// <summary>
    /// If set, a GET to this URL (with <see cref="HealthCheckTimeoutSeconds"/> timeout)
    /// must return 2xx before this entry is considered viable. Intended for local
    /// providers like Ollama that may not be running.
    /// </summary>
    public string? HealthCheckUrl            { get; set; }
    public int     HealthCheckTimeoutSeconds { get; set; } = 2;
}

// ── Interface ─────────────────────────────────────────────────────────────────

public interface ILlmFallbackChain
{
    bool   Enabled      { get; }
    string FallbackNote { get; }

    /// <summary>
    /// Returns the ordered list of viable (Provider, Model) fallback pairs.
    /// Entries whose <see cref="LlmFallbackEntry.HealthCheckUrl"/> is set are
    /// probed first and omitted if unreachable.
    /// </summary>
    Task<IReadOnlyList<(LlmProvider Provider, string Model)>> GetViableFallbacksAsync(
        CancellationToken ct = default);
}

// ── Implementation ────────────────────────────────────────────────────────────

public sealed class LlmFallbackChain : ILlmFallbackChain
{
    private readonly LlmFallbackSettings _settings;
    private readonly IHttpClientFactory  _httpFactory;

    public LlmFallbackChain( IOptions<LlmFallbackSettings> settings
                           , IHttpClientFactory             httpFactory )
    {
        _settings    = settings.Value;
        _httpFactory = httpFactory;
    }

    public bool   Enabled      => _settings.Enabled;
    public string FallbackNote => _settings.FallbackNote;

    public async Task<IReadOnlyList<(LlmProvider Provider, string Model)>> GetViableFallbacksAsync(
        CancellationToken ct = default)
    {
        var viable = new List<(LlmProvider, string)>();

        foreach (var entry in _settings.Chain)
        {
            if (!Enum.TryParse<LlmProvider>(entry.Provider, ignoreCase: true, out var provider))
                continue;

            if (entry.HealthCheckUrl is not null
             && !await IsReachableAsync(entry.HealthCheckUrl, entry.HealthCheckTimeoutSeconds, ct))
                continue;

            viable.Add((provider, entry.Model));
        }

        return viable;
    }

    private async Task<bool> IsReachableAsync(string url, int timeoutSeconds, CancellationToken ct)
    {
        try
        {
            using var client = _httpFactory.CreateClient();
            using var cts    = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

            using var request  = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await client.SendAsync(request
                                                      , HttpCompletionOption.ResponseHeadersRead
                                                      , cts.Token);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
