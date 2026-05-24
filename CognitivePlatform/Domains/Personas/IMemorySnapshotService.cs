using CognitivePlatform.Api.Domains.Personas.Models;

namespace CognitivePlatform.Api.Domains.Personas;

public interface IMemorySnapshotService
{
    Task<MemorySnapshot>              CreateSnapshotAsync( Guid              personaId
                                                         , string            name
                                                         , string            notes
                                                         , CancellationToken cancellationToken = default );

    Task                              LoadSnapshotAsync( Guid              snapshotId
                                                       , string            conversationId
                                                       , CancellationToken cancellationToken = default );

    Task<IReadOnlyList<MemorySnapshot>> GetSnapshotsAsync( Guid              personaId
                                                          , CancellationToken cancellationToken = default );

    Task                              UnloadSnapshotAsync( string            conversationId
                                                         , CancellationToken cancellationToken = default );
}
