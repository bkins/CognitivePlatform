using CognitivePlatform.Api.Domains.Personas.Models;

namespace CognitivePlatform.Api.Domains.Personas;

public class PersonaService : IPersonaService
{
    private readonly IPersonaStore _store;

    public PersonaService(IPersonaStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<CanonicalPersona> CreateAsync( string            name
                                                   , string?           scenarioDescription
                                                   , CancellationToken cancellationToken = default )
    {
        var now     = DateTime.UtcNow;
        var persona = new CanonicalPersona
                      {
                              Id                  = Guid.NewGuid()
                            , Name                = name.Trim()
                            , ScenarioDescription = scenarioDescription?.Trim()
                            , Configuration       = PersonaConfiguration.Default
                            , Personality         = new PersonalityProfile()
                            , RelationshipState   = new RelationshipModel()
                            , EmotionalState      = new EmotionalModel()
                            , Timeline            = new NarrativeTimeline()
                            , CreatedUtc          = now
                            , LastModifiedUtc     = now
                      };

        await _store.SaveAsync(persona, cancellationToken);

        return persona;
    }

    public Task<CanonicalPersona?> GetAsync(Guid personaId, CancellationToken cancellationToken = default)
        => _store.GetAsync(personaId, cancellationToken);

    public Task<IReadOnlyList<CanonicalPersona>> ListAsync(CancellationToken cancellationToken = default)
        => _store.GetAllAsync(cancellationToken);

    public async Task<PersonaMemory> AddMemoryAsync( Guid              personaId
                                                   , string            content
                                                   , MemoryType        type
                                                   , bool              userAsserted
                                                   , CancellationToken cancellationToken = default )
    {
        var persona = await _store.GetAsync(personaId, cancellationToken);

        if (persona is null)
            throw new InvalidOperationException($"Persona '{personaId}' not found.");

        var now    = DateTime.UtcNow;
        var memory = new PersonaMemory
                     {
                             Id              = Guid.NewGuid()
                           , PersonaId       = personaId
                           , Content         = content.Trim()
                           , Type            = type
                           , State           = MemoryState.Provisional
                           , Source          = userAsserted ? MemorySource.UserFragment : MemorySource.AIInferred
                           , UserAsserted    = userAsserted
                           , AIInferred      = !userAsserted
                           , Confidence      = userAsserted ? 1.0f : 0.5f
                           , CreatedUtc      = now
                           , LastModifiedUtc = now
                     };

        await _store.AddMemoryAsync(memory, cancellationToken);

        return memory;
    }

    public Task UpdateMemoryStateAsync( Guid              memoryId
                                      , Guid              personaId
                                      , MemoryState       newState
                                      , CancellationToken cancellationToken = default )
        => _store.UpdateMemoryStateAsync(memoryId, personaId, newState, cancellationToken);

    public async Task<MemorySnapshot> CreateSnapshotAsync( Guid              personaId
                                                         , string            name
                                                         , string?           notes
                                                         , CancellationToken cancellationToken = default )
    {
        var persona = await _store.GetAsync(personaId, cancellationToken);

        if (persona is null)
            throw new InvalidOperationException($"Persona '{personaId}' not found.");

        var memories = await _store.GetMemoriesAsync(personaId, cancellationToken);

        var snapshot = new MemorySnapshot
                       {
                               Id               = Guid.NewGuid()
                             , PersonaId        = personaId
                             , Name             = name.Trim()
                             , Configuration    = persona.Configuration
                             , IncludedMemories = memories.Select(memory => memory.Id).ToList()
                             , Notes            = notes?.Trim() ?? string.Empty
                             , CreatedUtc       = DateTime.UtcNow
                       };

        await _store.SaveSnapshotAsync(snapshot, cancellationToken);

        return snapshot;
    }

    public Task DeleteAsync(Guid personaId, CancellationToken cancellationToken = default)
        => _store.SoftDeleteAsync(personaId, cancellationToken);
}
