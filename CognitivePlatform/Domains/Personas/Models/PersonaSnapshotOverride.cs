namespace CognitivePlatform.Api.Domains.Personas.Models;

public record PersonaSnapshotOverride(
    Guid                       SnapshotId
  , PersonaConfiguration       Configuration
  , IReadOnlyList<PersonaMemory> Memories );
