using System.Text;
using System.Text.Json;
using CognitivePlatform.Api.Avails.Models;
using CognitivePlatform.Api.Interpreter;

using Microsoft.Extensions.Configuration;

namespace CognitivePlatform.Api.Avails;

public sealed class LlmStartupProbe
{
    private readonly ILlmClient               _llm;
    private readonly LlmModelCatalog          _catalog;
    private readonly ILogger<LlmStartupProbe> _log;

    public bool ShouldProbeModels { get; set; } = false;

    public LlmStartupProbe (ILlmClient               llm
                          , LlmModelCatalog          catalog
                          , IConfiguration           config
                          , ILogger<LlmStartupProbe> log)
    {
        _llm     = llm;
        _catalog = catalog;
        _log     = log;

        var shouldProbeConfig = config["ShouldProbe"];
        if (bool.TryParse(shouldProbeConfig, out var parsedShouldProbe))
        {
            ShouldProbeModels = parsedShouldProbe;
        }
        else
        {
#if DEBUG
            ShouldProbeModels = true;
#endif
        }
    }

    public async Task RunAsync (IEnumerable<string> candidateModels
                              , CancellationToken   ct)
    {

        if (ShouldProbeModels)
        {
            await ProbeModels(candidateModels
                            , ct);
        }
    }

    public async Task RunAsync( string            candidateModel
                              , CancellationToken ct )
    {
        if (ShouldProbeModels)
        {
            await ProbeModels(candidateModel
                            , ct);
        }
    }
    private async Task ProbeModels( IEnumerable<string> candidateModels
                                  , CancellationToken   ct )
    {

        _log.LogInformation("Starting Llm Startup Probe...");

        var results = new List<LlmModelInfo>();

        foreach (var model in candidateModels)
        {
            _log.LogInformation($"Probing {model}...");
            var probe = await _llm.ProbeAsync(model, ct);

            results.Add(new LlmModelInfo(model
                                       , probe.IsUsable
                                       , probe.Error
                                       , SupportsChat: probe.IsUsable
                                       , SupportsStreaming: probe.IsUsable));
        }

        foreach (var model in results)
            _catalog.Add(model);

        LogSummary(results);
    }

    private async Task ProbeModels( string            candidateModel
                                  , CancellationToken ct )
    {
        _log.LogInformation("Starting Llm Startup Probe...");

        _log.LogInformation($"Probing {candidateModel}...");
        var probe = await _llm.ProbeAsync(candidateModel
                                        , ct);

        var result = new LlmModelInfo(candidateModel
                                    , probe.IsUsable
                                    , probe.Error
                                    , SupportsChat: probe.IsUsable
                                    , SupportsStreaming: probe.IsUsable);

        _catalog.Add(result);

        LogSummary(result);
    }

    private void LogSummary( IEnumerable<LlmModelInfo> models )
    {
        _log.LogInformation("LLM Startup Probe Results:");

        foreach (var model in models)
        {
            if (model.IsUsable)
            {
                _log.LogInformation("  ✔ {Model} [OK]"
                                  , model.Name);
            }
            else
            {
                _log.LogWarning("  ✖ {ModelName} [FAILED] — {FailureReason}"
                              , model.Name
                              , FormatFailureReason(model.FailureReason));
            }
        }

    }

    private void LogSummary( LlmModelInfo model )
    {
        _log.LogInformation("LLM Startup Probe Results:");

        if (model.IsUsable)
        {
            _log.LogInformation("  ✔ {Model} [OK]"
                              , model.Name);
        }
        else
        {
            _log.LogWarning("  ✖ {ModelName} [FAILED] — {FailureReason}"
                          , model.Name
                          , FormatFailureReason(model.FailureReason));
        }

    }

    private static string FormatFailureReason(string? failureReason)
    {
        if (string.IsNullOrWhiteSpace(failureReason))
        {
            return "Unknown error";
        }

        var jsonText = failureReason.Trim();
        var prefix = string.Empty;

        var colonIndex = jsonText.IndexOf(':');
        if (colonIndex > 0 && jsonText.Substring(0, colonIndex).Trim().StartsWith("HTTP", StringComparison.OrdinalIgnoreCase))
        {
            prefix = jsonText.Substring(0, colonIndex + 1).Trim() + " ";
            jsonText = jsonText.Substring(colonIndex + 1).Trim();
        }

        try
        {
            using var doc = JsonDocument.Parse(jsonText);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
            {
                root = root[0];
            }

            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("error", out var errorProp))
                {
                    if (errorProp.ValueKind == JsonValueKind.Object)
                    {
                        if (errorProp.TryGetProperty("message", out var messageProp) && messageProp.ValueKind == JsonValueKind.String)
                        {
                            return $"{prefix}{CleanMessage(messageProp.GetString())}";
                        }
                    }
                    else if (errorProp.ValueKind == JsonValueKind.String)
                    {
                        return $"{prefix}{CleanMessage(errorProp.GetString())}";
                    }
                }

                if (root.TryGetProperty("message", out var directMessageProp) && directMessageProp.ValueKind == JsonValueKind.String)
                {
                    return $"{prefix}{CleanMessage(directMessageProp.GetString())}";
                }
            }
        }
        catch
        {
            // Fall through to cleaning raw text if JSON parsing fails
        }

        return $"{prefix}{CleanMessage(jsonText)}";
    }

    private static string CleanMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "Unknown error";
        }

        // Replace newlines with spaces to keep it single-line
        var result = message.Replace("\r", " ").Replace("\n", " ").Trim();

        // Collapse multiple spaces
        while (result.Contains("  "))
        {
            result = result.Replace("  ", " ");
        }

        // Truncate if excessively long
        if (result.Length > 500)
        {
            result = result.Substring(0, 497) + "...";
        }

        return result;
    }
}
