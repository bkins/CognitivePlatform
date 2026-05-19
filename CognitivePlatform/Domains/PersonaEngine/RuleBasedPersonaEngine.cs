using CognitivePlatform.Api.Domains.Personality;
using CognitivePlatform.Api.Domains.PersonaEngine.Models;

namespace CognitivePlatform.Api.Domains.PersonaEngine;

public class RuleBasedPersonaEngine : IPersonaEngine, IIntentAnalyzer
{
    private readonly IPersonalityService _personalityService;

    public RuleBasedPersonaEngine(IPersonalityService personalityService)
    {
        _personalityService = personalityService ?? throw new ArgumentNullException(nameof(personalityService));
    }

    public Task<IntentAnalysisResult> AnalyzeAsync(string message, CancellationToken ct = default)
        => Task.FromResult(ClassifyIntent(message));

    public async Task<PersonaContextResult> ResolveAsync(string userMessage, CancellationToken ct = default)
    {
        var analysisResult = ClassifyIntent(userMessage);
        var personality    = await PersonalityResolver.ResolveAsync(_personalityService, analysisResult.SuggestedPersonaName).ConfigureAwait(false);

        return new PersonaContextResult
               {
                   Personality          = personality
                 , Intent               = analysisResult.Intent
                 , IntentAnalysisResult = analysisResult
               };
    }

    // --- Private helpers -------------------------------------------------------

    private static IntentAnalysisResult ClassifyIntent(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return new IntentAnalysisResult
                   {
                       Intent     = Intent.Unknown
                     , Confidence = 0.0
                   };
        }

        var lowered = message.ToLowerInvariant();

        if (lowered.Contains("code")
         || lowered.Contains("bug")
         || lowered.Contains("compile")
         || lowered.Contains("error"))
        {
            return new IntentAnalysisResult
                   {
                       Intent               = Intent.TechnicalHelp
                     , Confidence           = 0.9
                     , SuggestedPersonaName = "TechnicalHelper"
                   };
        }

        if (lowered.Contains("team")
         || lowered.Contains("lead")
         || lowered.Contains("manage")
         || lowered.Contains("conflict"))
        {
            return new IntentAnalysisResult
                   {
                       Intent               = Intent.Leadership
                     , Confidence           = 0.85
                     , SuggestedPersonaName = "LeadershipCoach"
                   };
        }

        if (lowered.Contains("motivate")
         || lowered.Contains("inspire")
         || lowered.Contains("encourage")
         || lowered.Contains("burnout"))
        {
            return new IntentAnalysisResult
                   {
                       Intent               = Intent.Motivation
                     , Confidence           = 0.8
                     , SuggestedPersonaName = "Motivator"
                   };
        }

        if (lowered.Contains("how do i")
         || lowered.Contains("what is")
         || lowered.Contains("explain"))
        {
            return new IntentAnalysisResult
                   {
                       Intent               = Intent.GeneralHelp
                     , Confidence           = 0.7
                     , SuggestedPersonaName = "GeneralHelper"
                   };
        }

        return new IntentAnalysisResult
               {
                   Intent     = Intent.Unknown
                 , Confidence = 0.1
               };
    }

}
