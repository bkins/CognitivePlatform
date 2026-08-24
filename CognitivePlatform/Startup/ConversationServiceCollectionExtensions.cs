using CognitivePlatform.Api.Domains.Conversations;

namespace CognitivePlatform.Api.Startup;

public static class ConversationServiceCollectionExtensions
{
    public static IServiceCollection AddConversationServices(this IServiceCollection services)
    {
        services.AddSingleton<ITranscriptionService, LocalAudioTranscriptionService>();
        services.AddSingleton<ISpeakerDiarizationService, LocalSpeakerDiarizationService>();
        services.AddSingleton<IConversationService, ConversationService>();
        return services;
    }
}
