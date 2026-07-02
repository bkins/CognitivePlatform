using CognitivePlatform.Api.Domains.Media;

namespace CognitivePlatform.Api.Startup;

/// <summary>
/// Registers media attachment storage. Media files live under C:\CP\Data\{env}\Media,
/// alongside (but not inside) the SQLite data directory used by the persistence layer.
/// </summary>
public static class MediaServiceCollectionExtensions
{
    public static IServiceCollection AddMediaServices(this IServiceCollection services, IWebHostEnvironment environment)
    {
        services.Configure<MediaAttachmentSettings>(options =>
        {
            options.MediaRootPath = Path.Combine(@"C:\CP\Data"
                                               , environment.EnvironmentName
                                               , "Media");
        });

        services.AddSingleton<IMediaFileStorage, LocalMediaFileStorage>();
        services.AddSingleton<IMediaAttachmentService, MediaAttachmentService>();
        services.AddTransient<MediaActions>();

        return services;
    }
}
