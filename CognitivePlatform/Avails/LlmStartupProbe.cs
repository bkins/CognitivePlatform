using System.Text;
using System.Text.Json;
using CognitivePlatform.Api.Avails.Models;
using CognitivePlatform.Api.Interpreter;
using Microsoft.Extensions.Configuration;
using CP.Shared.Primitives.Avails.Extensions;

namespace CognitivePlatform.Api.Avails;

public sealed class LlmStartupProbe
{
    private const int    maxResultLength = 500;
    private const string ellipsis        = "...";

    private readonly ILlmClient               _llm;
    private readonly LlmModelCatalog          _catalog;
    private readonly ILogger<LlmStartupProbe> _log;
    private readonly LlmClientSettings?       _settings;

    public bool ShouldProbeModels { get; set; } = false;

    public LlmStartupProbe( ILlmClient               llm
                          , LlmModelCatalog          catalog
                          , IConfiguration           config
                          , ILogger<LlmStartupProbe> log )
    {
        _llm      = llm;
        _catalog  = catalog;
        _log      = log;
        _settings = config.GetSection("LlmClient").Get<LlmClientSettings>();

        var shouldProbeConfig = config["ShouldProbe"];
        if (bool.TryParse(shouldProbeConfig
                        , out var parsedShouldProbe))
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

    public async Task RunAsync( IEnumerable<string> candidateModels
                              , CancellationToken   ct )
    {

        if (ShouldProbeModels) await ProbeModels(candidateModels, ct);
    }

    public async Task RunAsync( string            candidateModel
                              , CancellationToken ct )
    {
        if (ShouldProbeModels) await ProbeModels(candidateModel, ct);
    }

    private async Task ProbeModels( IEnumerable<string> candidateModels
                                  , CancellationToken   ct )
    {

        _log.LogInformation("Starting Llm Startup Probe...");

        var results = new List<LlmModelInfo>();

        foreach (var model in candidateModels)
        {
            _log.LogInformation($"Probing {model}...");
            var probe = await _llm.ProbeAsync(model
                                            , ct);

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

        if (probe.IsUsable.Not())
        {
            var alternatives = GetAlternativeModelsForProvider(candidateModel);
            if (alternatives.Count > 0)
            {
                _log.LogWarning("  ✖ {ModelName} failed startup probe. Attempting to probe available alternative models for provider..."
                              , candidateModel);

                foreach (var altModel in alternatives)
                {
                    _log.LogInformation("Probing alternative {Model}..."
                                      , altModel);
                    var altProbe = await _llm.ProbeAsync(altModel
                                                       , ct);
                    if (altProbe is null)
                    {
                        continue;
                    }

                    var altResult = new LlmModelInfo(altModel
                                                   , altProbe.IsUsable
                                                   , altProbe.Error
                                                   , SupportsChat: altProbe.IsUsable
                                                   , SupportsStreaming: altProbe.IsUsable);

                    _catalog.Add(altResult);
                    LogSummary(altResult);

                    if (!altProbe.IsUsable) continue;

                    _log.LogInformation("  ✔ Successfully found working alternative model {Model} for provider."
                                      , altModel);

                    break;
                }
            }
        }
    }

    private IReadOnlyList<string> GetAlternativeModelsForProvider( string originalModel )
    {
        var alternatives = new List<string>();
        if (_settings is null) return alternatives;

        var provider = _settings.Provider;

        // 1. Gather matching models from SortedAllowedModels
        if (_settings.SortedAllowedModels is not null)
        {
            foreach (var cleanedModel in
                     from model in _settings.SortedAllowedModels
                     where !ModelBelongsToProvider(model, provider).Not()
                     select NormalizeModelName(model)
                     into cleanedModel
                     where cleanedModel.EqualsIgnoreCase(originalModel).Not()
                        && alternatives.Contains(cleanedModel).Not()
                     select cleanedModel)
            {
                alternatives.Add(cleanedModel);
            }
        }

        // 2. Add standard known models as fallbacks
        var standardFallbacks = GetStandardFallbackModels(provider);

        foreach (var model in standardFallbacks)
        {
            if (model.IsNotEqualTo(originalModel)
             && alternatives.Contains(model).Not())
            {
                alternatives.Add(model);
            }
        }

        return alternatives;
    }

    private static string NormalizeModelName( string model )
    {
        return model.Trim().Replace(" ", "-").ToLowerInvariant();
    }

    private static bool ModelBelongsToProvider( string      model
                                              , LlmProvider provider )
    {
        var modelLower = model.ToLowerInvariant();

        return provider switch
        {
                LlmProvider.Gemini => modelLower.Contains("gemini")
              , LlmProvider.Groq => modelLower.Contains("llama")
                                 || modelLower.Contains("mixtral")
                                 || modelLower.Contains("gemma")
                                 || modelLower.Contains("qwen")
                                 || modelLower.Contains("deepseek")
              , LlmProvider.Cerebras => modelLower.Contains("llama") || modelLower.Contains("cerebras")
              , LlmProvider.Ollama => modelLower.Contains("qwen")
                                   || modelLower.Contains("llama")
                                   || modelLower.Contains("phi")
                                   || modelLower.Contains("mistral")
                                   || modelLower.Contains("gemma")
                                   || modelLower.Contains(":")
              , LlmProvider.OpenRouter => modelLower.Contains("/")
                                       || modelLower.Contains("openai")
                                       || modelLower.Contains("google")
                                       || modelLower.Contains("anthropic")
              , _ => false
        };
    }

    private static IReadOnlyList<string> GetStandardFallbackModels( LlmProvider provider )
    {
        return provider switch
        {
                LlmProvider.Gemini => new[]
                                      {
                                              "gemini-2.5-flash"
                                            , "gemini-2.5-flash-lite"
                                            , "gemini-2.0-flash"
                                            , "gemini-1.5-flash"
                                            , "gemini-3.1-flash-lite"
                                            , "gemini-3.1-pro"
                                      }
              , LlmProvider.Groq => new[]
                                    {
                                            "llama-3.3-70b-versatile"
                                          , "llama-3.1-8b-instant"
                                          , "qwen-qwq-32b"
                                          , "deepseek-r1-distill-llama-70b"
                                    }
              , LlmProvider.Cerebras => new[]
                                        {
                                                "llama3.1-8b"
                                              , "llama-3.3-70b"
                                        }
              , LlmProvider.Ollama => new[]
                                      {
                                              "qwen2.5:14b"
                                            , "llama3.1:8b"
                                            , "llama3.2"
                                            , "phi3:mini"
                                      }
              , LlmProvider.OpenRouter => new[]
                                          {
                                                  "openai/gpt-4o-mini"
                                                , "google/gemini-2.5-flash"
                                          }
              , _ => Array.Empty<string>()
        };
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

    private static string FormatFailureReason( string? failureReason )
    {
        if (failureReason?.HasNoValue() ?? true) return "Unknown error";

        var jsonText   = failureReason.Trim();
        var prefix     = string.Empty;
        var colonIndex = jsonText.IndexOf(':');

        if (colonIndex > 0
         && jsonText[..colonIndex].Trim().StartsWithIgnoreCase("HTTP"))
        {
            prefix   = jsonText[..(colonIndex + 1)].Trim() + " ";
            jsonText = jsonText[(colonIndex + 1)..].Trim();
        }

        try
        {
            using var doc  = JsonDocument.Parse(jsonText);
            var       root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
            {
                root = root[0];
            }

            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("error"
                                      , out var errorProp))
                {
                    switch (errorProp.ValueKind)
                    {
                        case JsonValueKind.Object when errorProp.TryGetProperty("message", out var messageProp)
                                                    && messageProp.ValueKind == JsonValueKind.String:
                            return $"{prefix}{CleanMessage(messageProp.GetString())}";

                        case JsonValueKind.String:
                            return $"{prefix}{CleanMessage(errorProp.GetString())}";

                        case JsonValueKind.Undefined:
                        case JsonValueKind.Array:
                        case JsonValueKind.Number:
                        case JsonValueKind.True:
                        case JsonValueKind.False:
                        case JsonValueKind.Null:
                            break;

                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                }

                if (root.TryGetProperty("message", out var directMessageProp)
                 && directMessageProp.ValueKind == JsonValueKind.String)
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

    private static string CleanMessage( string? message )
    {
        if (message?.HasNoValue() ?? true) return "Unknown error";

        // Replace newlines with spaces to keep it single-line
        var result = message.Replace("\r", " ")
                            .Replace("\n", " ")
                            .Trim();

        // Collapse multiple spaces
        while (result.Contains("  "))
        {
            result = result.Replace("  ", " ");
        }

        // Truncate if excessively long
        if (result.Length > maxResultLength)
        {
            result = result[..(maxResultLength - ellipsis.Length)]
                   + ellipsis;
        }

        return result;
    }
}
