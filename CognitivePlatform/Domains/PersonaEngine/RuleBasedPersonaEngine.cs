using CognitivePlatform.Api.Domains.Personality;
using CognitivePlatform.Api.Domains.PersonaEngine.Models;

namespace CognitivePlatform.Api.Domains.PersonaEngine;

public class RuleBasedPersonaEngine : IPersonaEngine
{
    private readonly IPersonalityService _personalityService;

    public RuleBasedPersonaEngine(IPersonalityService personalityService)
    {
        _personalityService = personalityService ?? throw new ArgumentNullException(nameof(personalityService));
    }

    public async Task<PersonaContextResult> ResolveAsync(string userMessage, CancellationToken ct = default)
    {
        var analysisResult = ClassifyIntent(userMessage);
        var personality    = await ResolvePersonalityAsync(analysisResult.SuggestedPersonaName).ConfigureAwait(false);

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

    private async Task<PersonalityDefinition?> ResolvePersonalityAsync(string? suggestedPersonaName)
    {
        var allPersonalities = await _personalityService.GetAllAsync().ConfigureAwait(false);

        if (suggestedPersonaName is not null)
        {
            var normalizedSuggestion = NormalizeName(suggestedPersonaName);

            var matchedPersonality = allPersonalities.FirstOrDefault(personality =>
            {
                var normalizedName = NormalizeName(personality.Name);
                return normalizedName == normalizedSuggestion
                    || normalizedName.Contains(normalizedSuggestion)
                    || normalizedSuggestion.Contains(normalizedName);
            });

            if (matchedPersonality is not null)
                return matchedPersonality;
        }

        var activePersonality = await _personalityService.GetActiveAsync().ConfigureAwait(false);

        return activePersonality ?? allPersonalities.FirstOrDefault();
    }

    private static string NormalizeName(string name)
        => new string(name.ToLowerInvariant()
                          .Where(char.IsLetterOrDigit)
                          .ToArray());
}
