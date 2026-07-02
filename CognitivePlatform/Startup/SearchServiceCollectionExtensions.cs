using CognitivePlatform.Api.Domains.Document;
using CognitivePlatform.Api.Domains.Search;

namespace CognitivePlatform.Api.Startup;

/// <summary>
/// Registers Semantic Search and Document Indexing. Combined into one domain extension
/// since document indexing exists to feed semantic search and the two were adjacent,
/// small blocks in the original file.
/// </summary>
public static class SearchServiceCollectionExtensions
{
    public static IServiceCollection AddSearchServices(this IServiceCollection services)
    {
        services.AddTransient<SemanticSearchActions>();

        services.AddSingleton<DocumentChunkingService>();
        services.AddSingleton<IDocumentIndexingService, DocumentIndexingService>();
        services.AddTransient<DocumentActions>();

        return services;
    }
}
