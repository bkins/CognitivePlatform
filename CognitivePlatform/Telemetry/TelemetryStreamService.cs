using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using CognitivePlatform.Api.Telemetry.Events;

namespace CognitivePlatform.Api.Telemetry;

public interface ITelemetryStreamService
{
    void Publish(TelemetryEvent telemetryEvent);
    IAsyncEnumerable<TelemetryEvent> SubscribeAsync(CancellationToken cancellationToken);
}

public sealed class TelemetryStreamService : ITelemetryStreamService
{
    private readonly ConcurrentDictionary<Guid, Channel<TelemetryEvent>> _subscribers = new();

    public void Publish(TelemetryEvent telemetryEvent)
    {
        foreach (var sub in _subscribers.Values)
        {
            sub.Writer.TryWrite(telemetryEvent);
        }
    }

    public async IAsyncEnumerable<TelemetryEvent> SubscribeAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateBounded<TelemetryEvent>(new BoundedChannelOptions(100)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });

        _subscribers.TryAdd(id, channel);

        try
        {
            while (await channel.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                while (channel.Reader.TryRead(out var evt))
                {
                    yield return evt;
                }
            }
        }
        finally
        {
            _subscribers.TryRemove(id, out _);
        }
    }
}
