using CognitivePlatform.Api.Actions;
using CognitivePlatform.Api.Domains.Feedback;
using CognitivePlatform.Api.Domains.System;
using Microsoft.Extensions.Options;

namespace CognitivePlatform.Api.Startup;

/// <summary>
/// Registers system info services and the loose cross-cutting Actions (Feedback, System,
/// DebugFastPath) that the original file listed under a generic "Actions" block without
/// a clear domain home. Also wires up Scalar/OpenAPI, which is host-level plumbing rather
/// than a true domain, but is grouped here since it's tightly coupled to SystemService for
/// the title binding (environment name).
/// </summary>
public static class SystemServiceCollectionExtensions
{
    public static IServiceCollection AddSystemServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<ISystemInfoService, SystemService>();
        services.Configure<SystemPathsOptions>(configuration.GetSection("SystemPaths"));

        services.AddSingleton(sp =>
        {
            var environment = sp.GetRequiredService<IHostEnvironment>();

            return new SystemService(environment
                                    , sp.GetRequiredService<IOptions<SystemPathsOptions>>());
        });

        services.AddTransient<SystemActions>();
        services.AddTransient<FeedbackActions>();
        services.AddTransient<DebugFastPath>();

        services.AddEndpointsApiExplorer();
        services.AddOpenApi();

        return services;
    }
}
