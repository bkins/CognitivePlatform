using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Integrations.Embeddings;
using CognitivePlatform.Api.Services;
using CognitivePlatform.Api.SystemInfo;
using CognitivePlatform.Api.Training;

namespace CognitivePlatform.Api.Startup;

/// <summary>
/// Registers the SQLite-backed persistence layer (object store, vector store, idempotency
/// store, training telemetry store) and ensures the data directory exists.
/// </summary>
public static class PersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddDataPersistenceLayer(this IServiceCollection services, IWebHostEnvironment environment)
    {
        // ADM-05: DB lives at C:\CP\Data\{env}\ — outside the deploy tree so
        // that a clean-wipe of the deploy folder can never destroy production data.
        var dataDirectory = Path.Combine(@"C:\CP\Data", environment.EnvironmentName);
        Directory.CreateDirectory(dataDirectory);

        var dbPath = Path.Combine(dataDirectory, "platform.db");
        var connectionString = $"Data Source={dbPath};Cache=Shared;Mode=ReadWriteCreate;Pooling=True";

        var objectStore = new SqliteObjectStore(connectionString);
        services.AddSingleton<IObjectStore>(objectStore);
        services.AddSingleton(objectStore);
        services.AddSingleton<StartupInvariantGuard>();

        services.AddSingleton<IIdempotencyStore, ObjectStoreIdempotencyStore>();
        services.AddHostedService<IdempotencyEvictionService>();

        services.AddSingleton<IVectorStore>(_ => new SqliteVectorStore(connectionString));

        // ENH-23 Phase 3: training telemetry side-channel — separate table, same DB
        services.AddSingleton<IInterpreterTrainingStore>(
            _ => new SqliteInterpreterTrainingStore(connectionString));

        return services;
    }
}
