using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Domains.Personas.Models;

namespace CognitivePlatform.Api.Domains.Personas;

public class AlternateTimelineService : IAlternateTimelineService
{
    private readonly IObjectStore _store;

    public AlternateTimelineService(IObjectStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<AlternateTimeline> CreateTimelineAsync( Guid              personaId
                                                            , string            name
                                                            , string            premise
                                                            , DateTime          branchPointUtc
                                                            , CancellationToken cancellationToken = default )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var timeline = new AlternateTimeline
                       {
                               Id             = Guid.NewGuid()
                             , PersonaId      = personaId
                             , Name           = name
                             , Premise        = premise
                             , BranchPointUtc = branchPointUtc
                             , CreatedUtc     = DateTime.UtcNow
                       };

        await _store.Save(timeline
                        , partitionKey: $"timeline:{personaId}"
                        , id:           timeline.Id.ToString());

        return timeline;
    }

    public Task<IReadOnlyList<AlternateTimeline>> GetTimelinesForPersonaAsync( Guid              personaId
                                                                             , CancellationToken cancellationToken = default )
    {
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<AlternateTimeline> timelines =
            _store.List<AlternateTimeline>(partitionKey: $"timeline:{personaId}")
                  .Where(timeline => !timeline.IsDeleted)
                  .OrderBy(timeline => timeline.CreatedUtc)
                  .ToList();

        return Task.FromResult(timelines);
    }

    public Task<AlternateTimeline?> GetTimelineAsync( Guid              timelineId
                                                    , CancellationToken cancellationToken = default )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var allTimelines = _store.List<AlternateTimeline>();

        var timeline = allTimelines.FirstOrDefault(candidate =>
            candidate.Id == timelineId && !candidate.IsDeleted);

        return Task.FromResult(timeline);
    }

    public async Task<AlternateTimeline> AddEventAsync( Guid              timelineId
                                                       , TimelineEvent     timelineEvent
                                                       , CancellationToken cancellationToken = default )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var timeline = await GetTimelineAsync(timelineId, cancellationToken);

        if (timeline is null)
            throw new InvalidOperationException($"Timeline '{timelineId}' not found.");

        if (timelineEvent.Id == Guid.Empty)
            timelineEvent.Id = Guid.NewGuid();

        timeline.Events.Add(timelineEvent);

        await _store.Save(timeline
                        , partitionKey: $"timeline:{timeline.PersonaId}"
                        , id:           timeline.Id.ToString());

        return timeline;
    }
}
