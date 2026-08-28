using CognitivePlatform.Api.Domains.Conversations;

namespace CognitivePlatform.Api.Startup;

public static class ConversationServiceCollectionExtensions
{
    public static IServiceCollection AddConversationServices(this IServiceCollection services)
    {
        services.AddSingleton<ITranscriptionService, LocalAudioTranscriptionService>();
        services.AddSingleton<ISpeakerDiarizationService, LocalSpeakerDiarizationService>();
        services.AddSingleton<IConversationAnalyzer, LlmConversationAnalyzer>();
        services.AddSingleton<IConversationMemoryExtractor, LlmConversationMemoryExtractor>();
        services.AddSingleton<IConversationService, ConversationService>();
        services.AddTransient<ConversationActions>();
        return services;
    }
}
