using CognitivePlatform.Api.Avails;
using CognitivePlatform.Api.Domains.Personality;
using CognitivePlatform.Api.Domains.PersonaEngine.Models;

using Microsoft.Extensions.DependencyInjection;

namespace CognitivePlatform.Api.Domains.PersonaEngine;

public class HybridPersonaEngine : IPersonaEngine
{
    private readonly IIntentAnalyzer    _ruleBasedAnalyzer;
    private readonly IIntentAnalyzer    _keywordAnalyzer;
    private readonly IIntentAnalyzer    _llmAnalyzer;
    private readonly IPersonalityService _personalityService;

    private const double LlmWeight      = 0.5;
    private const double KeywordWeight  = 0.3;
    private const double RuleWeight     = 0.2;
    private const double WinnerThreshold = 0.4;

    // Cheap pre-gate: any of these keywords signals possible persona intent.
    // Mirrors DefaultKeywordRules so the gate stays in sync with the keyword analyzer.
    private static readonly string[] PersonaKeywords =
    {
        "code", "bug", "compile", "error"
      , "team", "lead", "manage", "conflict"
      , "motivate", "inspire", "encourage", "burnout"
    };

    public HybridPersonaEngine(
        [FromKeyedServices(KeyedServices.RuleBasedIntentAnalyzer)] IIntentAnalyzer  ruleBasedAnalyzer
      , [FromKeyedServices(KeyedServices.KeywordIntentAnalyzer)]   IIntentAnalyzer  keywordAnalyzer
      , [FromKeyedServices(KeyedServices.LlmIntentAnalyzer)]       IIntentAnalyzer  llmAnalyzer
      , IPersonalityService                                                          personalityService)
    {
        _ruleBasedAnalyzer  = ruleBasedAnalyzer  ?? throw new ArgumentNullException(nameof(ruleBasedAnalyzer));
        _keywordAnalyzer    = keywordAnalyzer    ?? throw new ArgumentNullException(nameof(keywordAnalyzer));
        _llmAnalyzer        = llmAnalyzer        ?? throw new ArgumentNullException(nameof(llmAnalyzer));
        _personalityService = personalityService ?? throw new ArgumentNullException(nameof(personalityService));
    }

    public async Task<PersonaContextResult> ResolveAsync(string userMessage, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userMessage))
        {
            return new PersonaContextResult
                   {
                       Intent               = Intent.Unknown
                     , IntentAnalysisResult = new IntentAnalysisResult { Intent = Intent.Unknown, Confidence = 0.0 }
                   };
        }

        // Keyword pre-gate: if no persona keyword appears in the input, skip all three
        // analyzers — including the LLM — and return Unknown immediately. This avoids
        // a full LLM round-trip on every non-FastPath turn when persona mode is inactive.
        if (!HasAnyPersonaKeyword(userMessage))
        {
            return new PersonaContextResult
                   {
                       Intent               = Intent.Unknown
                     , IntentAnalysisResult = new IntentAnalysisResult { Intent = Intent.Unknown, Confidence = 0.0 }
                   };
        }

        var ruleTask    = _ruleBasedAnalyzer.AnalyzeAsync(userMessage, ct);
        var keywordTask = _keywordAnalyzer.AnalyzeAsync(userMessage, ct);
        var llmTask     = _llmAnalyzer.AnalyzeAsync(userMessage, ct);

        await Task.WhenAll(ruleTask, keywordTask, llmTask).ConfigureAwait(false);

        var ruleResult    = ruleTask.Result;
        var keywordResult = keywordTask.Result;
        var llmResult     = llmTask.Result;

        var scores = new Dictionary<Intent, double>();

        AddWeightedScore(scores, ruleResult.Intent,    ruleResult.Confidence,    RuleWeight);
        AddWeightedScore(scores, keywordResult.Intent, keywordResult.Confidence, KeywordWeight);
        AddWeightedScore(scores, llmResult.Intent,     llmResult.Confidence,     LlmWeight);

        var (winnerIntent, winnerScore) = scores
            .OrderByDescending(pair => pair.Value)
            .First();

        if (winnerScore < WinnerThreshold || winnerIntent == Intent.Unknown)
        {
            return new PersonaContextResult
                   {
                       Intent               = Intent.Unknown
                     , IntentAnalysisResult = new IntentAnalysisResult
                                             {
                                                 Intent     = Intent.Unknown
                                               , Confidence = 0.0
                                               , Metadata   = BuildMetadata(ruleResult, keywordResult, llmResult)
                                             }
                   };
        }

        var suggestedName = PickSuggestedPersonaName(winnerIntent, llmResult, keywordResult, ruleResult);

        var winnerAnalysis = new IntentAnalysisResult
                             {
                                 Intent               = winnerIntent
                               , Confidence           = winnerScore
                               , SuggestedPersonaName = suggestedName
                               , Metadata             = BuildMetadata(ruleResult, keywordResult, llmResult)
                             };

        var personality = await PersonalityResolver.ResolveAsync(_personalityService, suggestedName).ConfigureAwait(false);

        return new PersonaContextResult
               {
                   Personality          = personality
                 , Intent               = winnerIntent
                 , IntentAnalysisResult = winnerAnalysis
               };
    }

    // --- Private helpers -------------------------------------------------------

    private static void AddWeightedScore(
        Dictionary<Intent, double> scores
      , Intent                     intent
      , double                     confidence
      , double                     weight)
    {
        if (!scores.TryGetValue(intent, out var current))
            current = 0.0;

        scores[intent] = current + (confidence * weight);
    }

    private static string? PickSuggestedPersonaName(
        Intent              winner
      , IntentAnalysisResult llmResult
      , IntentAnalysisResult keywordResult
      , IntentAnalysisResult ruleResult)
    {
        if (llmResult.Intent == winner && llmResult.SuggestedPersonaName is not null)
            return llmResult.SuggestedPersonaName;

        if (keywordResult.Intent == winner && keywordResult.SuggestedPersonaName is not null)
            return keywordResult.SuggestedPersonaName;

        return ruleResult.Intent == winner ? ruleResult.SuggestedPersonaName : null;
    }

    private static Dictionary<string, string> BuildMetadata(
        IntentAnalysisResult ruleResult
      , IntentAnalysisResult keywordResult
      , IntentAnalysisResult llmResult)
    {
        return new Dictionary<string, string>
               {
                   ["RuleIntent"]    = ruleResult.Intent.ToString()
                 , ["RuleConf"]      = ruleResult.Confidence.ToString("F2")
                 , ["KeywordIntent"] = keywordResult.Intent.ToString()
                 , ["KeywordConf"]   = keywordResult.Confidence.ToString("F2")
                 , ["LlmIntent"]     = llmResult.Intent.ToString()
                 , ["LlmConf"]       = llmResult.Confidence.ToString("F2")
               };
    }

    private static bool HasAnyPersonaKeyword(string userMessage)
    {
        foreach (var keyword in PersonaKeywords)
        {
            if (userMessage.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
