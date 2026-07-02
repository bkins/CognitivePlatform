using CognitivePlatform.Api.Domains.DailyRecord;

namespace CognitivePlatform.Api.Startup;

/// <summary>
/// Registers the Daily Record domain.
/// </summary>
public static class DailyRecordServiceCollectionExtensions
{
    public static IServiceCollection AddDailyRecordServices(this IServiceCollection services)
    {
        services.AddSingleton<IDailyRecordCommandParser, DailyRecordCommandParser>();
        services.AddSingleton<IDailyRecordService, DailyRecordService>();
        services.AddTransient<DailyRecordActions>();

        return services;
    }
}
