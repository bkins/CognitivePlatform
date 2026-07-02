using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Insights;
using CognitivePlatform.Api.Workspace;

namespace CognitivePlatform.Api.Startup;

/// <summary>
/// Registers the Automation Gate (Phase E). Resolves dependencies for dynamic
/// user settings verification and audit logging.
/// </summary>
public static class AutomationGateServiceCollectionExtensions
{
    public static IServiceCollection AddAutomationGateServices(this IServiceCollection services)
    {
        services.AddSingleton<IAutomationGate>(sp =>
        {
            var settingsService = sp.GetRequiredService<IUserSettingsService>();
            var store           = sp.GetRequiredService<IObjectStore>();
            var logger          = sp.GetRequiredService<ILogger<AutomationGate>>();

            return new AutomationGate(settingsService, store, logger);
        });

        return services;
    }
}
