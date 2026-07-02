using CognitivePlatform.Api.Domains.BrainDump;

namespace CognitivePlatform.Api.Startup;

/// <summary>
/// Registers the Brain Dump domain.
/// </summary>
public static class BrainDumpServiceCollectionExtensions
{
    public static IServiceCollection AddBrainDumpServices(this IServiceCollection services)
    {
        services.AddSingleton<IBrainDumpService, BrainDumpService>();

        return services;
    }
}
