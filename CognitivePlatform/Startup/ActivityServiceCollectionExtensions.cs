using CognitivePlatform.Api.Domains.Activity;

namespace CognitivePlatform.Api.Startup;

/// <summary>
/// Registers the Activity domain.
/// </summary>
public static class ActivityServiceCollectionExtensions
{
    public static IServiceCollection AddActivityServices(this IServiceCollection services)
    {
        services.AddSingleton<IActivityLog, ObjectStoreActivityLog>();
        services.AddTransient<ActivityActions>();

        return services;
    }
}
