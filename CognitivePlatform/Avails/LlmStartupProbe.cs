using System.Text;
using CognitivePlatform.Api.Avails.Models;
using CognitivePlatform.Api.Interpreter;

namespace CognitivePlatform.Api.Avails;

public sealed class LlmStartupProbe
{
    private readonly ILlmClient               _llm;
    private readonly LlmModelCatalog          _catalog;
    private readonly ILogger<LlmStartupProbe> _log;

    public bool ShouldProbeModels { get; set; } = true;
    
    public LlmStartupProbe (ILlmClient               llm
                          , LlmModelCatalog          catalog
                          , ILogger<LlmStartupProbe> log)
    {
        _llm     = llm;
        _catalog = catalog;
        _log     = log;
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

    public async Task RunAsync( string candidateModel
                              , CancellationToken   ct )
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
    // private void LogSummary (IEnumerable<LlmModelInfo> models)
    // {
    //     var sb = new StringBuilder();
    //
    //     sb.AppendLine("LLM Startup Probe Summary:");
    //     sb.AppendLine("--------------------------------");
    //
    //     foreach (var model in models.OrderBy(m => m.Name))
    //     {
    //         if (model.IsUsable)
    //         {
    //             sb.AppendLine($"✔ {model.Name} [OK]");
    //         }
    //         else
    //         {
    //             sb.AppendLine($"✖ {model.Name} [FAILED] — {model.FailureReason}");
    //         }
    //     }
    //
    //     sb.AppendLine("--------------------------------");
    //     
    //     _log.LogInformation("LLM Startup Probe Summary {@Models}"
    //                       , models.Select(model => new
    //                                                {
    //                                                        model.Name
    //                                                      , model.IsUsable
    //                                                      , model.FailureReason
    //                                                }));
    //
    // }

}
