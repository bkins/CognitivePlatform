using CognitivePlatform.Api.Domains.PersonaEngine.Models;

namespace CognitivePlatform.Api.Domains.PersonaEngine;

public class KeywordIntentAnalyzer : IIntentAnalyzer
{
    private readonly IReadOnlyDictionary<string, (Intent Intent, string? SuggestedPersonaName)> _rules;

    public KeywordIntentAnalyzer(IReadOnlyDictionary<string, (Intent, string?)> rules)
    {
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));
    }

    public Task<IntentAnalysisResult> AnalyzeAsync(string message, CancellationToken ct = default)
    {
        if (message.HasNoValue())
        {
            return Task.FromResult(new IntentAnalysisResult
                                   {
                                       Intent     = Intent.Unknown
                                     , Confidence = 0.0
                                   });
        }

        foreach (var (keyword, (intent, personaName)) in _rules)
        {
            if (message.ContainsIgnoreCase(keyword))
            {
                return Task.FromResult(new IntentAnalysisResult
                                       {
                                           Intent               = intent
                                         , Confidence           = 0.8
                                         , SuggestedPersonaName = personaName
                                       });
            }
        }

        return Task.FromResult(new IntentAnalysisResult
                               {
                                   Intent     = Intent.GeneralHelp
                                 , Confidence = 0.5
                               });
    }
}
