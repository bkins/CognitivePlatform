using CognitivePlatform.Api.Domains.Personas.Models;

namespace CognitivePlatform.Api.Domains.Personas;

public interface IPersonaBehaviorPolicy
{
    PersonaConfiguration ApplyGuardrails(PersonaConfiguration configuration);
    bool                 IsResurrectionZone(PersonaConfiguration configuration);
}
