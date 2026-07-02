using CognitivePlatform.Api.Domains.Identity;

namespace CognitivePlatform.Api.Startup;

/// <summary>
/// Registers the Identity domain.
/// </summary>
public static class IdentityServiceCollectionExtensions
{
    public static IServiceCollection AddIdentityServices(this IServiceCollection services)
    {
        services.AddSingleton<IIdentityService, IdentityService>();
        services.AddSingleton<IIdentityAnalysisService, IdentityAnalysisService>();
        services.AddTransient<IdentityActions>();

        return services;
    }
}
