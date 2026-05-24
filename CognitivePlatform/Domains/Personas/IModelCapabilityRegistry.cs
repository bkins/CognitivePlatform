using CognitivePlatform.Api.Domains.Personas.Models;

namespace CognitivePlatform.Api.Domains.Personas;

public interface IModelCapabilityRegistry
{
    IReadOnlyList<ModelCapabilityProfile> GetAll();
    ModelCapabilityProfile?               GetByModel(string modelName);
    IReadOnlyList<ModelCapabilityProfile> GetByRestrictionLevel(ProviderRestrictionLevel level);
}
