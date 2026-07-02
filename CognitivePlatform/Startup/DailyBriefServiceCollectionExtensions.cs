using CognitivePlatform.Api.Domains.Tasks;
using CognitivePlatform.Api.Integrations.Notifications;
using CognitivePlatform.Api.Services;

namespace CognitivePlatform.Api.Startup;

/// <summary>
/// Registers the Daily Brief service and the Notification pattern/schedule services.
/// Combined into one domain extension since notification scheduling exists primarily to
/// drive the daily brief, and they were adjacent, small blocks in the original file.
/// </summary>
public static class DailyBriefServiceCollectionExtensions
{
    public static IServiceCollection AddDailyBriefServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IDailyBriefService, DailyBriefService>();

        services.Configure<NotificationSettings>(configuration.GetSection("Notifications"));
        services.AddSingleton<INotificationPatternService, NotificationPatternService>();
        services.AddSingleton<INotificationScheduleProvider, PatternAwareNotificationScheduleService>();

        return services;
    }
}
