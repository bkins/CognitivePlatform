using CognitivePlatform.Api.Domains.Cognition;
using CognitivePlatform.Api.Domains.Wellbeing;
using CognitivePlatform.Api.Wellbeing;

namespace CognitivePlatform.Api.Startup;

/// <summary>
/// Registers the Wellbeing domain.
/// </summary>
public static class WellbeingServiceCollectionExtensions
{
    public static IServiceCollection AddWellbeingServices(this IServiceCollection services)
    {
        services.AddSingleton<IWellbeingSignalStore, WellbeingSignalStore>();
        services.AddSingleton<IWellbeingSignalCollector, WellbeingSignalCollector>();
        services.AddSingleton<IWellbeingPatternService, WellbeingPatternService>();
        services.AddTransient<WellbeingActions>();
        services.AddTransient<CognitionActions>();

        return services;
    }
}
