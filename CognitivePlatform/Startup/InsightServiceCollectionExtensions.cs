using CognitivePlatform.Api.Insights;
using CognitivePlatform.Api.Insights.Models;

namespace CognitivePlatform.Api.Startup;

/// <summary>
/// Registers the Insight Engine: all <see cref="IInsightProvider"/> implementations
/// (Phase B baseline + Phase C task awareness + ENH-31 Reflective Intelligence Phase Two),
/// plus the <see cref="InsightPolicy"/> with its enforced default repeat windows.
/// </summary>
public static class InsightServiceCollectionExtensions
{
    public static IServiceCollection AddInsightServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IInsightProvider, ConversationReflectionInsightProvider>();
        services.AddScoped<IInsightProvider, JournalActivityInsightProvider>();
        services.AddScoped<IInsightProvider, TaskAwarenessInsightProvider>();
        services.AddScoped<IInsightProvider, StressPatternInsightProvider>();
        services.AddScoped<IInsightProvider, OverdueTasksNoJournalInsightProvider>();

        // ENH-31 — Reflective Intelligence Phase Two
        services.AddScoped<IInsightProvider, HealthCorrelationInsightProvider>();
        services.AddScoped<IInsightProvider, GoalAlignmentInsightProvider>();
        services.AddScoped<IInsightProvider, HabitReinforcementInsightProvider>();
        services.AddScoped<IInsightProvider, CognitiveDistortionInsightProvider>();
        services.AddScoped<IInsightProvider, MealInsightProvider>();

        services.AddScoped<IInsightEngine, InsightEngine>();
        services.AddSingleton<IInsightHistoryStore, ObjectStoreInsightHistoryStore>();
        services.AddScoped<Domains.Insights.IPatternDataAggregator, Domains.Insights.PatternDataAggregator>();
        services.AddTransient<Domains.Insights.InsightsActions>();
        services.AddHostedService<Services.OffPeakInsightService>();

        var insightPolicy = configuration.GetSection("Insights").Get<InsightPolicy>()
                          ?? new InsightPolicy();

        // Enforce the 72-hour repeat window for the Habit category (stress-pattern coaching
        // provider) unless the operator has explicitly configured a different window in appsettings.
        if (!insightPolicy.CategoryRepeatWindows.ContainsKey(InsightCategory.Habit))
        {
            insightPolicy.CategoryRepeatWindows[InsightCategory.Habit] = TimeSpan.FromHours(72);
        }

        // Enforce the 72-hour repeat window for the Health category (health correlation
        // provider) unless the operator has explicitly configured a different window.
        if (!insightPolicy.CategoryRepeatWindows.ContainsKey(InsightCategory.Health))
        {
            insightPolicy.CategoryRepeatWindows[InsightCategory.Health] = TimeSpan.FromHours(72);
        }

        services.AddSingleton(insightPolicy);

        services.AddScoped<INotificationEngine, NotificationEngine>();

        return services;
    }
}
