using CognitivePlatform.Api.Domains.PersonaEngine.Models;

namespace CognitivePlatform.Api.Domains.PersonaEngine;

public interface IPersonaEngine
{
    Task<PersonaContextResult> ResolveAsync(string userMessage, CancellationToken ct = default);
}
