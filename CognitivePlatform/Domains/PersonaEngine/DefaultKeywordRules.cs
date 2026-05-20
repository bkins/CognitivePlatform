namespace CognitivePlatform.Api.Domains.PersonaEngine;

internal static class DefaultKeywordRules
{
    internal static IReadOnlyDictionary<string, (Intent, string?)> Build()
    {
        return new Dictionary<string, (Intent, string?)>(StringComparer.OrdinalIgnoreCase)
               {
                   ["code"]      = (Intent.TechnicalHelp, "TechnicalHelper")
                 , ["bug"]       = (Intent.TechnicalHelp, "TechnicalHelper")
                 , ["compile"]   = (Intent.TechnicalHelp, "TechnicalHelper")
                 , ["error"]     = (Intent.TechnicalHelp, "TechnicalHelper")
                 , ["team"]      = (Intent.Leadership, "LeadershipCoach")
                 , ["lead"]      = (Intent.Leadership, "LeadershipCoach")
                 , ["manage"]    = (Intent.Leadership, "LeadershipCoach")
                 , ["conflict"]  = (Intent.Leadership, "LeadershipCoach")
                 , ["motivate"]  = (Intent.Motivation, "Motivator")
                 , ["inspire"]   = (Intent.Motivation, "Motivator")
                 , ["encourage"] = (Intent.Motivation, "Motivator")
                 , ["burnout"]   = (Intent.Motivation, "Motivator")
               };
    }
}
