using CognitivePlatform.Api.Domains.Identity;
using CognitivePlatform.Api.Domains.MemoryReview;

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
        services.AddScoped<IMemoryReviewService, MemoryReviewService>();
        services.AddTransient<IdentityActions>();

        return services;
    }
}
