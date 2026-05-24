using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Domains.Personas.Models;

namespace CognitivePlatform.Api.Domains.Personas;

public class EmotionalTopologyTracker : IEmotionalTopologyTracker
{
    private readonly IObjectStore _store;

    public EmotionalTopologyTracker(IObjectStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task RecordSampleAsync( EmotionalTopologyPoint point
                                       , CancellationToken      cancellationToken = default )
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (point.Id == Guid.Empty)
            point.Id = Guid.NewGuid();

        if (point.SampledUtc == default)
            point.SampledUtc = DateTime.UtcNow;

        await _store.Save(point
                        , partitionKey: $"topology:{point.PersonaId}"
                        , id:           point.Id.ToString());
    }

    public Task<IEnumerable<EmotionalTopologyPoint>> GetTopologyAsync( Guid              personaId
                                                                      , CancellationToken cancellationToken = default )
    {
        cancellationToken.ThrowIfCancellationRequested();

        IEnumerable<EmotionalTopologyPoint> points =
            _store.List<EmotionalTopologyPoint>(partitionKey: $"topology:{personaId}")
                  .OrderBy(point => point.SampledUtc)
                  .ToList();

        return Task.FromResult(points);
    }

    public Task<EmotionalTopologyPoint?> GetLatestSampleAsync( Guid              personaId
                                                              , CancellationToken cancellationToken = default )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var latest = _store.List<EmotionalTopologyPoint>(partitionKey: $"topology:{personaId}")
                           .OrderByDescending(point => point.SampledUtc)
                           .FirstOrDefault();

        return Task.FromResult(latest);
    }
}
