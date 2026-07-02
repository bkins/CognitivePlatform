using CognitivePlatform.Api.Domains.Journal;
using CognitivePlatform.Api.Domains.Tasks;
using CognitivePlatform.Api.Domains.Knowledge;
using CognitivePlatform.Api.KnowledgeInbox;
using CognitivePlatform.Api.KnowledgeInbox.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace CognitivePlatform.Api.Startup;

/// <summary>
/// Registers the Knowledge Inbox domain and its source providers (Journal, Task).
/// </summary>
public static class KnowledgeInboxServiceCollectionExtensions
{
    public static IServiceCollection AddKnowledgeInboxServices(this IServiceCollection services)
    {
        services.AddSingleton<IKnowledgeService, KnowledgeService>();
        services.AddSingleton<IKnowledgeSource, JournalKnowledgeSource>();
        services.AddSingleton<IKnowledgeSource, TaskKnowledgeSource>();

        // Phase 4.6: Domain Expert Knowledge Mode
        services.AddSingleton<IKnowledgeIngestionService, KnowledgeIngestionService>();
        services.AddTransient<KnowledgeActions>();

        return services;
    }
}
