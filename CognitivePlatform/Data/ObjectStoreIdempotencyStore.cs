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

    public Task<ConverseResponse?> TryGetAsync(Guid clientRequestId, CancellationToken ct)
    {
        var id = clientRequestId.ToString("N");

        var record = _store.Get<ProcessedRequest>(id);

        if (record is null)
            return Task.FromResult<ConverseResponse?>(null);

        var response = JsonSerializer.Deserialize<ConverseResponse>(record.ResponseJson);

        return Task.FromResult(response);
    }

    public Task StoreAsync(Guid clientRequestId, ConverseResponse response, CancellationToken ct)
    {
        var id = clientRequestId.ToString("N");

        var record = new ProcessedRequest
                     {
                             Id = id
                           , ResponseJson = JsonSerializer.Serialize(response)
                           , CreatedUtc = DateTimeOffset.UtcNow
                     };

        _store.Save(record);

        return Task.CompletedTask;
    }
}
