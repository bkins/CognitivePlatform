using CognitivePlatform.Api.Domains.Personas;
using CognitivePlatform.Api.Domains.Personas.Models;

namespace CognitivePlatform.Api.Startup;

/// <summary>
/// Registers the Persona domain. The original Program.cs grew this in five dated phases
/// (base persona CRUD, memory reconstruction, multi-model rendering, advanced narrative
/// systems). Rather than flattening that history, each phase keeps its own method so the
/// "why" of each addition stays visible and any phase can be found/removed independently.
/// </summary>
public static class PersonaServiceCollectionExtensions
{
    public static IServiceCollection AddPersonaServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddPersonaCoreServices();
        services.AddPersonaMemoryReconstruction();
        services.AddPersonaMultiModelRendering(configuration);
        services.AddPersonaNarrativeSystems();

        return services;
    }

    private static IServiceCollection AddPersonaCoreServices(this IServiceCollection services)
    {
        services.AddScoped<IPersonaStore, PersonaStore>();
        services.AddScoped<IPersonaService, PersonaService>();
        services.AddScoped<IPersonaBehaviorPolicy, PersonaBehaviorPolicy>();
        services.AddScoped<IPersonaRuntime, PersonaRuntime>();
        services.AddSingleton<IPersonaSessionManager, PersonaSessionManager>();
        services.AddScoped<PersonaActions>();

        return services;
    }

    // Phase C: Memory Reconstruction Engine
    private static IServiceCollection AddPersonaMemoryReconstruction(this IServiceCollection services)
    {
        services.AddScoped<IMemoryReconstructionEngine, MemoryReconstructionEngine>();
        services.AddSingleton<IMemoryConfirmationQueue, MemoryConfirmationQueue>();

        return services;
    }

    // Phase D: Multi-Model Rendering
    private static IServiceCollection AddPersonaMultiModelRendering(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<List<ModelCapabilityProfile>>(
            configuration.GetSection("PersonaModelCapabilities"));

        services.AddSingleton<IModelCapabilityRegistry, ModelCapabilityRegistry>();
        services.AddSingleton<IPersonaStabilityTracker, PersonaStabilityTracker>();
        services.AddScoped<IPersonaModelSelector, PersonaModelSelector>();

        return services;
    }

    // Phase E: Advanced Narrative Systems
    private static IServiceCollection AddPersonaNarrativeSystems(this IServiceCollection services)
    {
        services.AddSingleton<IDreamModeAdapter, DreamModeAdapter>();
        services.AddSingleton<IAlternateTimelineService, AlternateTimelineService>();
        services.AddSingleton<IEmotionalTopologyTracker, EmotionalTopologyTracker>();
        services.AddScoped<IMemorySnapshotService, MemorySnapshotService>();

        return services;
    }
}
