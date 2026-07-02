using CognitivePlatform.Api.Domains.Health;
using CognitivePlatform.Api.Integrations.Health;

namespace CognitivePlatform.Api.Startup;

/// <summary>
/// Registers the Health domain. Uses <see cref="HttpHealthProvider"/> when a phone base URL
/// is configured under HealthConnect, otherwise falls back to a disconnected stub so the
/// app still starts cleanly with health integration off.
/// </summary>
public static class HealthServiceCollectionExtensions
{
    public static IServiceCollection AddHealthServices(this IServiceCollection services, IConfiguration configuration)
    {
        var healthConnectSection = configuration.GetSection("HealthConnect");
        services.Configure<HealthConnectSettings>(healthConnectSection);
        services.AddHttpClient("HealthConnect");

        var phoneBaseUrl = healthConnectSection.GetValue<string>(nameof(HealthConnectSettings.PhoneBaseUrl)) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(phoneBaseUrl))
            services.AddSingleton<IHealthProvider, HttpHealthProvider>();
        else
            services.AddSingleton<IHealthProvider, DisconnectedHealthProvider>();

        services.AddTransient<HealthActions>();

        return services;
    }
}
