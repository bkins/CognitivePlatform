using System.Text.Json;
using Xunit.Abstractions;

namespace CognitivePlatform.IntegrationTests.Infrastructure;

/// <summary>
/// Delegating HTTP handler that logs requests and responses to xunit output.
/// Activated only when the VERBOSE_INTEGRATION environment variable is "true".
/// Uses <see cref="HttpContent.LoadIntoBufferAsync()"/> to buffer each body
/// before reading so the content stream is still available to the test.
/// </summary>
internal sealed class VerboseLoggingHandler : DelegatingHandler
{
    private const int MaxLoggedBodyLength = 3_000;

    private readonly ITestOutputHelper _output;

    public VerboseLoggingHandler(ITestOutputHelper output)
        : base(new HttpClientHandler())
    {
        _output = output;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request
      , CancellationToken   ct)
    {
        _output.WriteLine($"→ {request.Method} {request.RequestUri}");

        if (request.Content is not null)
        {
            // Buffer so the stream can still be read by the inner handler after logging.
            await request.Content.LoadIntoBufferAsync();
            var requestBody = await request.Content.ReadAsStringAsync(ct);

            if (requestBody.Length > 0)
                _output.WriteLine($"  Request: {Truncate(TryPrettyPrint(requestBody))}");
        }

        var response = await base.SendAsync(request, ct);

        _output.WriteLine($"← {(int)response.StatusCode} {response.StatusCode}");

        // Buffer so downstream callers (ReadJsonAsync<T>) can read the body too.
        await response.Content.LoadIntoBufferAsync();
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (responseBody.Length > 0)
            _output.WriteLine($"  Response: {Truncate(TryPrettyPrint(responseBody))}");

        _output.WriteLine(string.Empty);

        return response;
    }

    // ----------------------------------------------------------------
    // Helpers
    // ----------------------------------------------------------------

    private static string TryPrettyPrint(string input)
    {
        try
        {
            using var document = JsonDocument.Parse(input);
            return JsonSerializer.Serialize(document, new JsonSerializerOptions
            {
                WriteIndented = true
            });
        }
        catch (JsonException)
        {
            return input;
        }
    }

    private static string Truncate(string value)
        => value.Length <= MaxLoggedBodyLength
               ? value
               : value[..MaxLoggedBodyLength] + $"\n  ... [{value.Length - MaxLoggedBodyLength} chars truncated]";
}
