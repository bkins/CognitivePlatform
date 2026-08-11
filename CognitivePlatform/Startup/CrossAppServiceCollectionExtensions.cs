using CognitivePlatform.Api.Integrations.CrossApp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CognitivePlatform.Api.Startup;

public static class CrossAppServiceCollectionExtensions
{
    public static IServiceCollection AddCrossAppIntegrations(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<CrossAppSettings>(configuration.GetSection("CrossApp"));

        services.AddSingleton<IExternalAppConnector, WatchListConnector>();
        services.AddSingleton<ExternalAppConnectorRegistry>();
        services.AddScoped<CrossAppActions>();

        return services;
    }
}
