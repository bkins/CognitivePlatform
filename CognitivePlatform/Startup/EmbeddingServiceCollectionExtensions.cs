using CognitivePlatform.Api.Integrations.Embeddings;
using CognitivePlatform.Api.Services;

namespace CognitivePlatform.Api.Startup;

/// <summary>
/// Registers the Embeddings domain. Uses <see cref="OllamaEmbeddingService"/> when an Ollama
/// base URL is configured under Embedding, otherwise falls back to a disconnected stub.
/// </summary>
public static class EmbeddingServiceCollectionExtensions
{
    public static IServiceCollection AddEmbeddingServices(this IServiceCollection services, IConfiguration configuration)
    {
        var embeddingSection = configuration.GetSection("Embedding");
        services.Configure<EmbeddingSettings>(embeddingSection);
        services.AddHttpClient("OllamaEmbedding");

        var embeddingBaseUrl = embeddingSection.GetValue<string>(nameof(EmbeddingSettings.OllamaBaseUrl)) ?? string.Empty;
        if (embeddingBaseUrl.HasValue())
            services.AddSingleton<IEmbeddingService, OllamaEmbeddingService>();
        else
            services.AddSingleton<IEmbeddingService, DisconnectedEmbeddingService>();

        services.AddHostedService<EmbeddingBackfillService>();

        return services;
    }
}
