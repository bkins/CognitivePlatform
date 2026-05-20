using CognitivePlatform.Api.Domains.Personality;

namespace CognitivePlatform.Api.Domains.PersonaEngine;

internal static class PersonalityResolver
{
    internal static async Task<PersonalityDefinition?> ResolveAsync(
        IPersonalityService personalityService
      , string?             suggestedPersonaName)
    {
        var allPersonalities = await personalityService.GetAllAsync().ConfigureAwait(false);

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

        var activePersonality = await personalityService.GetActiveAsync().ConfigureAwait(false);

        return activePersonality ?? allPersonalities.FirstOrDefault();
    }

    private static string NormalizeName(string name)
        => new string(name.ToLowerInvariant()
                          .Where(char.IsLetterOrDigit)
                          .ToArray());
}
