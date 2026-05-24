using CognitivePlatform.Api.Domains.Personas.Models;

namespace CognitivePlatform.Api.Domains.Personas;

public interface IEmotionalTopologyTracker
{
    Task                                      RecordSampleAsync( EmotionalTopologyPoint point
                                                               , CancellationToken      cancellationToken = default );

    Task<IEnumerable<EmotionalTopologyPoint>> GetTopologyAsync( Guid              personaId
                                                              , CancellationToken cancellationToken = default );

    Task<EmotionalTopologyPoint?>             GetLatestSampleAsync( Guid              personaId
                                                                   , CancellationToken cancellationToken = default );
}
