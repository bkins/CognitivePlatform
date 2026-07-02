using CognitivePlatform.Api.Domains.Tasks;

namespace CognitivePlatform.Api.Startup;

/// <summary>
/// Registers the Task domain.
/// </summary>
public static class TaskServiceCollectionExtensions
{
    public static IServiceCollection AddTaskServices(this IServiceCollection services)
    {
        services.AddSingleton<ITaskService, TaskService>();
        services.AddTransient<TaskActions>();
        services.AddTransient<TaskReasonerActions>();

        return services;
    }
}
