using CognitivePlatform.Api.Domains.Calendar;
using CognitivePlatform.Api.Integrations.Calendar;

namespace CognitivePlatform.Api.Startup;

/// <summary>
/// Registers the Calendar domain (Google Calendar provider, keyed by environment-specific
/// configuration section).
/// </summary>
public static class CalendarServiceCollectionExtensions
{
    public static IServiceCollection AddCalendarServices(this IServiceCollection services, IConfiguration configuration, string environmentName)
    {
        // FIX: the original Program.cs registered AddSingleton<ICalendarProvider, GoogleCalendarProvider>()
        // twice — once here, and once again later, right after AddControllers()/AddOpenApi().
        // The second call was a duplicate (same lifetime, same types) with no apparent reason
        // for the split, so it's been removed. Only one registration remains below.
        var googleCalendarSection = $"GoogleCalendar:{environmentName}";
        services.Configure<GoogleCalendarSettings>(configuration.GetSection(googleCalendarSection));
        services.AddHttpClient(googleCalendarSection);
        services.AddSingleton<ICalendarProvider, GoogleCalendarProvider>();
        services.AddTransient<CalendarActions>();

        return services;
    }
}
