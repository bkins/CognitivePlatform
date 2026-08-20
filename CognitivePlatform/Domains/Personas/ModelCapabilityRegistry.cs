using CognitivePlatform.Api.Domains.Personas.Models;
using Microsoft.Extensions.Options;

namespace CognitivePlatform.Api.Domains.Personas;

public class ModelCapabilityRegistry : IModelCapabilityRegistry
{
    private readonly IReadOnlyList<ModelCapabilityProfile> _profiles;

    public ModelCapabilityRegistry(IOptions<List<ModelCapabilityProfile>> options)
    {
        _profiles = (options?.Value ?? []).AsReadOnly();
    }

    public IReadOnlyList<ModelCapabilityProfile> GetAll()
        => _profiles;

    public ModelCapabilityProfile? GetByModel(string modelName)
        => _profiles.FirstOrDefault(profile => profile.ModelName.EqualsIgnoreCase(modelName));

    public IReadOnlyList<ModelCapabilityProfile> GetByRestrictionLevel(ProviderRestrictionLevel level)
        => _profiles
            .Where(profile => profile.RestrictionLevel == level)
            .ToList();
}
