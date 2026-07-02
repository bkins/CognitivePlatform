using CognitivePlatform.Api.Domains.FileSync;
using CognitivePlatform.Api.Integrations.FileSync;
using CP.Shared.Primitives.Avails.Extensions;

namespace CognitivePlatform.Api.Startup;

/// <summary>
/// Registers the File Sync domain. Uses <see cref="HttpFileSyncProvider"/> when a gateway
/// base URL is configured under FileSync, otherwise falls back to a disconnected stub.
/// </summary>
public static class FileSyncServiceCollectionExtensions
{
    public static IServiceCollection AddFileSyncServices(this IServiceCollection services, IConfiguration configuration)
    {
        var fileSyncSection = configuration.GetSection("FileSync");
        services.Configure<FileSyncSettings>(fileSyncSection);
        services.AddHttpClient("FileSync");

        var fileSyncGatewayBaseUrl = fileSyncSection.GetValue<string>(nameof(FileSyncSettings.GatewayBaseUrl)) ?? string.Empty;
        if (fileSyncGatewayBaseUrl.HasValue())
            services.AddSingleton<IFileSyncProvider, HttpFileSyncProvider>();
        else
            services.AddSingleton<IFileSyncProvider, DisconnectedFileSyncProvider>();

        services.AddSingleton<ILocalFileSystem, LocalFileSystem>();
        services.AddSingleton<IFileSyncService, FileSyncService>();
        services.AddTransient<FileSyncActions>();

        return services;
    }
}
