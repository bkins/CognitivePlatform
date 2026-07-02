using CognitivePlatform.Api.Domains.Journal;
using CognitivePlatform.Api.Domains.Journal.Capabilities;
using CognitivePlatform.Api.Domains.Journal.Interfaces;
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
    public static IServiceCollection AddJournalServices(this IServiceCollection services)
    {
        services.AddSingleton<IJournalService, JournalService>();
        services.AddSingleton<IJournalDraftRepository, InMemoryJournalDraftRepository>();
        services.AddSingleton<IJournalCommandParser, JournalCommandParser>();
        services.AddScoped<ICrudService<JournalEntryWithRevision>, JournalCrudServiceAdapter>();

        services.AddSingleton<IJournalRevisionRepository, JournalRevisionRepository>();

        services.AddTransient<JournalActions>();

        return services;
    }
}
