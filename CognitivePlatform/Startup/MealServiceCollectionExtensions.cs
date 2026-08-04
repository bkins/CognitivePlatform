using CognitivePlatform.Api.Domains.Meals;
using Microsoft.Extensions.DependencyInjection;

namespace CognitivePlatform.Api.Startup;

/// <summary>
/// Registers the Meal domain.
/// </summary>
public static class MealServiceCollectionExtensions
{
    public static IServiceCollection AddMealServices(this IServiceCollection services)
    {
        services.AddSingleton<IMealService, MealService>();
        services.AddTransient<MealActions>();

        return services;
    }
}
