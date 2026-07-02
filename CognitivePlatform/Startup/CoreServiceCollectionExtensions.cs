using CognitivePlatform.Api.Audit;
using CognitivePlatform.Api.Conversation;
using CognitivePlatform.Api.Execution;
using CognitivePlatform.Api.Orchestrator;
using CognitivePlatform.Api.Registry;
using CognitivePlatform.Api.Registry.Capabilities;
using CognitivePlatform.Api.Telemetry;
using CognitivePlatform.Api.Workspace;

namespace CognitivePlatform.Api.Startup;

/// <summary>
/// Registers workspace context and the small set of cross-cutting "core" services
/// (audit log, action/capability registries, conversation orchestration, telemetry).
/// These have no sub-phases and no conditional branching, so they're grouped together
/// rather than split into one-liner files per concern.
/// </summary>
public static class CoreServiceCollectionExtensions
{
    public static IServiceCollection AddWorkspaceServices(this IServiceCollection services)
    {
        services.AddTransient<WorkspaceActions>();
        services.AddSingleton<IUserSettingsService, UserSettingsService>();
        services.AddSingleton<IWorkspaceContext, WorkspaceContext>();

        return services;
    }

    public static IServiceCollection AddCoreServices(this IServiceCollection services)
    {
        services.AddSingleton<IAuditLog, ObjectStoreAuditLog>();
        services.AddSingleton<IActionRegistry, ActionRegistry>();
        services.AddSingleton<ICapabilityRegistry, CapabilityRegistry>();
        services.AddScoped<IConversationOrchestrator, ConversationOrchestrator>();
        services.AddScoped<IExecutionEngine, ExecutionEngine>();

        services.AddScoped<ConsoleTelemetrySink>();
        services.AddSingleton<ITelemetryStreamService, TelemetryStreamService>();
        services.AddScoped<ITelemetrySink, PersistentConversationTelemetrySink>();
        services.AddScoped<TelemetryContext>();
        services.AddSingleton<ITelemetryAggregatorService, ObjectStoreTelemetryAggregatorService>();

        services.AddSingleton<ConversationContextStore>();
        services.AddSingleton<IConversationTurnStore, ConversationTurnStore>();
        services.AddSingleton<IConversationMetadataStore, ConversationMetadataStore>();

        return services;
    }
}
