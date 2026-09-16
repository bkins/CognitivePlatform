using CognitivePlatform.Api.Domains.Journal;
using CognitivePlatform.Api.Domains.Journal.Capabilities;
using CognitivePlatform.Api.Domains.Journal.Interfaces;
using CognitivePlatform.Api.Domains.Journal.Import.Ttr;
using CognitivePlatform.Api.Models;
using CognitivePlatform.Api.Registry.Capabilities;

namespace CognitivePlatform.Api.Startup;

/// <summary>
/// Registers the Journal domain, including the revision history sub-store.
/// (The original Program.cs split "Journals" and "Journals-Revisions" into two comment
/// blocks — they're combined here since revisions have no independent existence apart
/// from the journal entries they version.)
/// </summary>
public static class JournalServiceCollectionExtensions
{
    public static IServiceCollection AddJournalServices(this IServiceCollection services, IWebHostEnvironment environment)
    {
        services.AddSingleton<IJournalService, JournalService>();
        services.AddSingleton<IJournalDraftRepository, InMemoryJournalDraftRepository>();
        services.AddSingleton<IJournalCommandParser, JournalCommandParser>();
        services.AddScoped<ICrudService<JournalEntryWithRevision>, JournalCrudServiceAdapter>();

        services.AddSingleton<IJournalRevisionRepository, JournalRevisionRepository>();
        services.AddSingleton<TtrContentNormalizer>();
        services.AddSingleton<TtrImportPlanner>();
        services.AddSingleton<ITtrMediaContentReader, TtrMediaContentReader>();
        services.AddSingleton<IHistoricalJournalWriter, HistoricalJournalWriter>();
        services.AddSingleton<TtrEmbeddingReindexService>();
        services.AddSingleton(provider => new TtrImportExecutor(provider.GetRequiredService<Data.IObjectStore>()
                                                              , provider.GetRequiredService<IHistoricalJournalWriter>()
                                                              , provider.GetRequiredService<Domains.Media.IMediaAttachmentService>()
                                                              , provider.GetRequiredService<ITtrMediaContentReader>()
                                                              , environment.EnvironmentName
                                                              , Path.Combine(@"C:\CP\Data", environment.EnvironmentName, "platform.db")));

        services.AddTransient<JournalActions>();

        return services;
    }
}
