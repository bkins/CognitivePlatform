using CognitivePlatform.Api.Avails;
using CognitivePlatform.Api.Domains.PersonaEngine;
using CognitivePlatform.Api.Domains.Personality;
using CognitivePlatform.Api.Interpreter;

namespace CognitivePlatform.Api.Startup;

/// <summary>
/// Registers the Personality service and the Persona Engine's intent-analysis pipeline
/// (rule-based, keyword-based, and LLM-based analyzers, selected by key).
/// This is distinct from <see cref="PersonaServiceCollectionExtensions"/>, which owns
/// persona data/runtime/memory rather than intent analysis.
/// </summary>
public static class PersonalityServiceCollectionExtensions
{
    public static IServiceCollection AddPersonalityServices(this IServiceCollection services)
    {
        services.AddSingleton<IPersonalityService, PersonalityService>();
        services.AddTransient<PersonalityActions>();
        services.AddTransient<PersonaEngineActions>();

        services.AddSingleton<RuleBasedPersonaEngine>();
        services.AddKeyedSingleton<IIntentAnalyzer>(KeyedServices.RuleBasedIntentAnalyzer
                                                   , (sp, _) => sp.GetRequiredService<RuleBasedPersonaEngine>());

        services.AddKeyedSingleton<IIntentAnalyzer, KeywordIntentAnalyzer>(
            KeyedServices.KeywordIntentAnalyzer
          , (sp, _) => new KeywordIntentAnalyzer(DefaultKeywordRules.Build()));

        services.AddKeyedSingleton<IIntentAnalyzer, LlmIntentAnalyzer>(
            KeyedServices.LlmIntentAnalyzer
          , (sp, _) => new LlmIntentAnalyzer(sp.GetRequiredService<ILlmRouter>()));

        services.AddSingleton<IPersonaEngine, HybridPersonaEngine>();

        return services;
    }
}
