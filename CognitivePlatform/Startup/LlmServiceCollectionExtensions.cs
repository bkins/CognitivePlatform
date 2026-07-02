using CognitivePlatform.Api.Avails;
using CognitivePlatform.Api.Conversation;
using CognitivePlatform.Api.Interpreter;
using CognitivePlatform.Api.Interpreter.FastPath;
using CognitivePlatform.Api.Registry.Capabilities;
using CognitivePlatform.Api.SystemPromptLogging;
using CognitivePlatform.Api.Telemetry;
using Microsoft.Extensions.Options;

namespace CognitivePlatform.Api.Startup;

/// <summary>
/// Registers LLM provider infrastructure: named HttpClients per provider, the capacity-aware
/// router/fallback chain, and the keyed IInterpreter registrations (mock + LLM-backed).
/// </summary>
public static class LlmServiceCollectionExtensions
{
    public static IServiceCollection AddLlmServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddLlmHttpClients();
        services.AddLlmSettings(configuration);
        services.AddLlmRoutingInfrastructure(configuration);
        services.AddLlmInterpreters();

        return services;
    }

    private static IServiceCollection AddLlmHttpClients(this IServiceCollection services)
    {
        services.AddHttpClient();
        services.AddHttpClient("Ollama");
        services.AddHttpClient("Groq");
        services.AddHttpClient("Gemini");
        services.AddHttpClient("OpenRouter");
        services.AddHttpClient("Cerebras");

        return services;
    }

    private static IServiceCollection AddLlmSettings(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<LlmClientSettings>(configuration.GetSection("LlmClient"));

        // CHANGE: LlmProviderDefaults no longer binds its own "Llm:Defaults" config section.
        // It now reads through LlmClientSettings (registered above) via constructor injection,
        // collapsing what used to be two separately-maintained sources for the same per-provider
        // model defaults. See LlmProviderDefaults.cs for details.
        services.AddSingleton<LlmProviderDefaults>();

        // ENH-08: bounded turn-history caps for ConversationContext (in-memory, session-scoped).
        // Defaults: MaxTurnHistory = 50, MaxTurnMessageLength = 4096.
        services.Configure<ConversationContextOptions>(configuration.GetSection("ConversationContext"));

        services.Configure<LlmFallbackSettings>(configuration.GetSection("LlmFallback"));

        return services;
    }

    private static IServiceCollection AddLlmRoutingInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Usage tracker must be registered before LlmClientFactory.
        services.AddSingleton<IGroqUsageTracker, GroqUsageTracker>();

        // EPIC-07: Provider Capacity & Routing — usage aggregator + rate limiter
        services.AddSingleton<ILlmUsageAggregator, InMemoryLlmUsageAggregator>();
        services.AddSingleton<ILlmRateLimiter, InMemoryLlmRateLimiter>();

        // EPIC-07 Phase B: capacity-aware multi-provider router.
        // FIX: the original registration captured `builder.Configuration` from the outer
        // closure instead of using the `configuration` parameter passed into this method.
        // That worked today only because both happened to reference the same instance —
        // it's a latent bug if this method is ever called with a different IConfiguration
        // (e.g. in a test host). Resolved here by using the parameter directly.
        services.AddSingleton<ILlmCapacityRouter>(_ =>
        {
            var configs = configuration.GetSection("LlmModels").Get<List<LlmModelConfig>>() ?? [];
            var rateLimiter = _.GetRequiredService<ILlmRateLimiter>();

            return new LlmCapacityRouter(configs, rateLimiter);
        });

        // Factory — selects the active provider at runtime
        services.AddSingleton<LlmClientFactory>();
        services.AddSingleton<ILlmClientFactory>(sp => sp.GetRequiredService<LlmClientFactory>());

        // ILlmClient — resolved via factory so swapping providers is a config change
        services.AddSingleton<ILlmClient>(sp => sp.GetRequiredService<LlmClientFactory>().Create());

        // LlmFallbackChain — explicit fallback sequence consulted on 429
        services.AddSingleton<ILlmFallbackChain, LlmFallbackChain>();

        // ILlmRouter — session-aware dispatcher in front of the factory.
        // Every call re-reads context.Metadata so SetProvider takes effect next turn.
        services.AddSingleton<ILlmRouter, LlmRouter>();

        services.AddSingleton<LlmModelCatalog>();
        services.AddSingleton<LlmStartupProbe>();

        return services;
    }

    private static IServiceCollection AddLlmInterpreters(this IServiceCollection services)
    {
        services.AddKeyedScoped<IInterpreter>(KeyedServices.MockInterpreter
                                             , (sp, key) => new MockInterpreter(sp.GetRequiredService<ICapabilityRegistry>()
                                                                              , sp.GetRequiredService<ITelemetrySink>()));

        services.AddScoped<IFastPathResolver, FastPathResolver>();

        // ENH-19 Phase B: rule-based task-complexity classifier. Drives the
        // tier preference the orchestrator forwards to the router.
        services.AddSingleton<ITaskComplexityClassifier, TaskComplexityClassifier>();

        services.AddKeyedScoped<IInterpreter>(KeyedServices.LlmInterpreter
                                             , (sp, _) => new LlmInterpreter(sp.GetRequiredService<ICapabilityRegistry>()
                                                                            , sp.GetRequiredService<ITelemetrySink>()
                                                                            , sp.GetRequiredService<ILlmRouter>()
                                                                            , sp.GetRequiredService<LlmModelCatalog>()
                                                                            , sp.GetRequiredService<IOptions<LlmClientSettings>>().Value
                                                                            , sp.GetRequiredService<IPromptLogger>()));

        return services;
    }
}
