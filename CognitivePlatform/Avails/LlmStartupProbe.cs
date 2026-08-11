using System.Text;
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
                _log.LogWarning("  ✖ {Model.Name} [FAILED] — {Model.FailureReason}"
                              , model.Name
                              , model.FailureReason ?? "Unknown error");
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
            _log.LogWarning("  ✖ {Model.Name} [FAILED] — {Model.FailureReason}"
                          , model.Name
                          , model.FailureReason ?? "Unknown error");
        }

    }
}
