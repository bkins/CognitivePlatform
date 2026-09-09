using CognitivePlatform.Api.Domains.Backlog;

namespace CognitivePlatform.Api.Startup;

public static class BacklogServiceCollectionExtensions
{
    public static IServiceCollection AddBacklogServices(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration["Backlog:ConnectionString"]
                               ?? @"Data Source=C:\CP\Data\Shared\Backlog\updated-backlog.db;Cache=Shared;Mode=ReadWriteCreate;Pooling=True";

        services.AddSingleton<IBacklogBoardService>(_ => new SqliteBacklogBoardService(connectionString));
        return services;
    }
}
