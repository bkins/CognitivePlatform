namespace CognitivePlatform.Api.Domains.Personality;

public interface IPersonalityService
{
    Task<IReadOnlyList<PersonalityDefinition>> GetAllAsync();

    Task<PersonalityDefinition?> GetActiveAsync();

    /// <summary>
    /// Sets the active personality by id. Deactivates all others and persists the change.
    /// Throws <see cref="KeyNotFoundException"/> when no personality with that id exists.
    /// </summary>
    Task<PersonalityDefinition> SetActiveAsync(string personalityId);

    Task<PersonalityDefinition> AddAsync(PersonalityDefinition personality);
}
