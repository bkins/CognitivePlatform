using CognitivePlatform.Api.Domains.Secrets;

namespace CognitivePlatform.Api.Startup;

public static class SecretsServiceCollectionExtensions
{
    public static IServiceCollection AddSecretsServices(this IServiceCollection services)
    {
        services.AddSingleton<ISecretVaultService, SecretVaultService>();
        services.AddTransient<SecretActions>();

        return services;
    }
}
