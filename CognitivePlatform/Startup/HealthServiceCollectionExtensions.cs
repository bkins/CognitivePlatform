using CognitivePlatform.Api.Domains.Health;
using CognitivePlatform.Api.Integrations.Health;

namespace CognitivePlatform.Api.Startup;

/// <summary>
/// Registers the Health domain services.
/// The push-based architecture means the CP API no longer polls the phone —
/// instead LAA pushes health snapshots to <c>POST /health/data</c>, which are stored
/// in <see cref="HealthDataCache"/> and read synchronously from <see cref="HealthActions"/>.
/// </summary>
public static class HealthServiceCollectionExtensions
{
    public static IServiceCollection AddHealthServices(this IServiceCollection services, IConfiguration configuration)
    {
        var healthConnectSection = configuration.GetSection("HealthConnect");
        services.Configure<HealthConnectSettings>(healthConnectSection);

        services.AddSingleton<HealthDataCache>();
        services.AddSingleton<IHealthProvider, CacheBackedHealthProvider>();
        services.AddTransient<HealthActions>();

        return services;
    }
}
