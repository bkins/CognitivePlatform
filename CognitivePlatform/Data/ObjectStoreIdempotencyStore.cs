using System.Text.Json;
using CognitivePlatform.Api.Contracts;

namespace CognitivePlatform.Api.Data;

public class ObjectStoreIdempotencyStore : IIdempotencyStore
{
    private readonly IObjectStore _store;

    public ObjectStoreIdempotencyStore(IObjectStore store)
    {
        _store = store;
    }

    public async Task<ConverseResponse?> TryGetAsync(Guid clientRequestId, CancellationToken ct)
    {
        var id     = clientRequestId.ToString("N");
        var record = await _store.GetAsync<ProcessedRequest>(id, cancellationToken: ct);

        if (record is null)
            return null;

        return JsonSerializer.Deserialize<ConverseResponse>(record.ResponseJson);
    }

    public Task StoreAsync(Guid clientRequestId, ConverseResponse response, CancellationToken ct)
    {
        var id = clientRequestId.ToString("N");
        var record = new ProcessedRequest
                     {
                             Id           = id
                           , ResponseJson = JsonSerializer.Serialize(response)
                           , CreatedUtc   = DateTimeOffset.UtcNow
                     };

        _store.Save(record);

        return Task.CompletedTask;
    }

    public async Task<int> EvictOlderThanAsync(TimeSpan maxAge, CancellationToken ct = default)
    {
        var cutoff = DateTimeOffset.UtcNow.Subtract(maxAge);
        var expired = await _store.ListAsync<ProcessedRequest>(toUtc: cutoff, cancellationToken: ct);
        int evictedCount = 0;

        foreach (var record in expired)
        {
            if (await _store.SoftDeleteAsync<ProcessedRequest>(record.Id, cancellationToken: ct))
                evictedCount++;
        }

        return evictedCount;
    }
}
