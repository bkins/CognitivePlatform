using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Telemetry.Events;
using CP.Shared.Primitives.Avails.Extensions;
using Microsoft.Extensions.Logging;

namespace CognitivePlatform.Api.Telemetry;

/// <summary>
/// Wraps <see cref="ConsoleTelemetrySink"/> and additionally persists
/// telemetry records to the Object Store so
/// <see cref="ObjectStoreTelemetryAggregatorService"/> can compute metrics
/// across restarts.
///
/// Persistence is fire-and-forget (the exception is swallowed) because
/// telemetry must never block or break a user-facing turn.
/// </summary>
public sealed class PersistentConversationTelemetrySink : ITelemetrySink
{
    private readonly ConsoleTelemetrySink                        _console;
    private readonly IObjectStore                                _store;
    private readonly ITelemetryStreamService                     _streamService;
    private readonly ILogger<PersistentConversationTelemetrySink> _logger;

    private const string PartitionKey = "telemetry";

    public PersistentConversationTelemetrySink(ConsoleTelemetrySink                        console
                                              , IObjectStore                                store
                                              , ITelemetryStreamService                     streamService
                                              , ILogger<PersistentConversationTelemetrySink> logger)
    {
        _console       = console       ?? throw new ArgumentNullException(nameof(console));
        _store         = store         ?? throw new ArgumentNullException(nameof(store));
        _streamService = streamService ?? throw new ArgumentNullException(nameof(streamService));
        _logger        = logger        ?? throw new ArgumentNullException(nameof(logger));
    }

    public void Track(TelemetryEvent telemetryEvent)
    {
        _console.Track(telemetryEvent);
        _streamService.Publish(telemetryEvent);

        var record = CreateRecord(telemetryEvent);
        if (record is not null)
        {
            _ = PersistAsync(record);
        }
    }

    public void Track(string line) => _console.Track(line);

    private static TelemetryRecord? CreateRecord(TelemetryEvent evt)
    {
        return evt switch
        {
            ConversationCompletedEvent completed => new TelemetryRecord
                                                    {
                                                        EventName    = completed.EventName
                                                      , SessionId    = completed.SessionId
                                                      , TimestampUtc = completed.TimestampUtc
                                                      , DurationMs   = completed.TimeElapsed.TotalMilliseconds
                                                      , Success      = true
                                                    }
          , ExecutionCompletedEvent exec         => new TelemetryRecord
                                                    {
                                                        EventName    = exec.ActionName.HasValue() ? $"Action.{exec.ActionName}" : exec.EventName
                                                      , SessionId    = exec.SessionId
                                                      , TimestampUtc = exec.TimestampUtc
                                                      , DurationMs   = ExtractDuration(exec)
                                                      , Success      = exec.Success
                                                    }
          , LlmInterpreterCompletedEvent llmComp => new TelemetryRecord
                                                    {
                                                        EventName    = llmComp.EventName
                                                      , SessionId    = llmComp.SessionId
                                                      , TimestampUtc = llmComp.TimestampUtc
                                                      , DurationMs   = llmComp.TimeElapsed.TotalMilliseconds
                                                      , Success      = true
                                                    }
          , LlmInterpreterErrorEvent llmErr     => new TelemetryRecord
                                                    {
                                                        EventName    = llmErr.EventName
                                                      , SessionId    = llmErr.SessionId
                                                      , TimestampUtc = llmErr.TimestampUtc
                                                      , DurationMs   = 0
                                                      , Success      = false
                                                    }
          , OrchestratorCompletedEvent orch      => new TelemetryRecord
                                                    {
                                                        EventName    = orch.EventName
                                                      , SessionId    = orch.SessionId
                                                      , TimestampUtc = orch.TimestampUtc
                                                      , DurationMs   = ExtractDuration(orch)
                                                      , Success      = true
                                                    }
          , IdempotencyHitEvent hit              => new TelemetryRecord
                                                    {
                                                        EventName    = hit.EventName
                                                      , SessionId    = hit.SessionId
                                                      , TimestampUtc = hit.TimestampUtc
                                                      , DurationMs   = 0
                                                      , Success      = true
                                                    }
          , _                                    => null
        };
    }

    private static double ExtractDuration(TelemetryEvent evt)
    {
        if (evt.Properties.TryGetValue("DurationMs", out var durObj) && durObj is not null)
        {
            if (durObj is double durationVal) return durationVal;
            if (double.TryParse(durObj.ToString(), out var parsedVal)) return parsedVal;
        }
        return 0;
    }

    private async Task PersistAsync(TelemetryRecord record)
    {
        try
        {
            await _store.Save(record, partitionKey: PartitionKey, id: record.Id)
                        .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist telemetry event {EventName} for session {SessionId}", record.EventName, record.SessionId);
        }
    }
}
