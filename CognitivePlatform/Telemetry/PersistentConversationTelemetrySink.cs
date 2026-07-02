using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Telemetry.Events;
using Microsoft.Extensions.Logging;

namespace CognitivePlatform.Api.Telemetry;

/// <summary>
/// Wraps <see cref="ConsoleTelemetrySink"/> and additionally persists
/// <see cref="ConversationCompletedEvent"/> records to the Object Store so
/// <see cref="ObjectStoreTelemetryAggregatorService"/> can compute metrics
/// across restarts.
///
/// Persistence is fire-and-forget (the exception is swallowed) because
/// telemetry must never block or break a user-facing turn.
/// </summary>
public sealed class PersistentConversationTelemetrySink : ITelemetrySink
{
    private readonly ConsoleTelemetrySink _console;
    private readonly IObjectStore         _store;
    private readonly ITelemetryStreamService _streamService;

    private const string PartitionKey = "telemetry";

    public PersistentConversationTelemetrySink( ConsoleTelemetrySink console
                                              , IObjectStore         store
                                              , ITelemetryStreamService streamService )
    {
        _console       = console ?? throw new ArgumentNullException(nameof(console));
        _store         = store   ?? throw new ArgumentNullException(nameof(store));
        _streamService = streamService ?? throw new ArgumentNullException(nameof(streamService));
    }

    public void Track(TelemetryEvent telemetryEvent)
    {
        _console.Track(telemetryEvent);
        _streamService.Publish(telemetryEvent);

        if (telemetryEvent is ConversationCompletedEvent completed)
            _ = PersistAsync(completed);
    }

    public void Track(string line) => _console.Track(line);

    private async Task PersistAsync(ConversationCompletedEvent evt)
    {
        try
        {
            var record = new TelemetryRecord
                         {
                                 EventName    = evt.EventName
                               , SessionId    = evt.SessionId
                               , TimestampUtc = evt.TimestampUtc
                               , DurationMs   = evt.TimeElapsed.TotalMilliseconds
                               , Success      = true
                         };

            await _store.Save(record, partitionKey: PartitionKey, id: record.Id)
                        .ConfigureAwait(false);
        }
        catch
        {
            // Telemetry persistence must never surface errors to the caller.
        }
    }
}
