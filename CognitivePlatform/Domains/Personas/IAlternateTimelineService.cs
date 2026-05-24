using CognitivePlatform.Api.Domains.Personas.Models;

namespace CognitivePlatform.Api.Domains.Personas;

public interface IAlternateTimelineService
{
    Task<AlternateTimeline>              CreateTimelineAsync( Guid              personaId
                                                           , string            name
                                                           , string            premise
                                                           , DateTime          branchPointUtc
                                                           , CancellationToken cancellationToken = default );

    Task<IReadOnlyList<AlternateTimeline>> GetTimelinesForPersonaAsync( Guid              personaId
                                                                      , CancellationToken cancellationToken = default );

    Task<AlternateTimeline?> GetTimelineAsync( Guid              timelineId
                                             , CancellationToken cancellationToken = default );

    Task<AlternateTimeline> AddEventAsync( Guid              timelineId
                                         , TimelineEvent     timelineEvent
                                         , CancellationToken cancellationToken = default );
}
