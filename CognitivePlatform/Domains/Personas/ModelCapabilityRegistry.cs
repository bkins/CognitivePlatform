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
        => _profiles.FirstOrDefault(profile => string.Equals(profile.ModelName
                                                            , modelName
                                                            , StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<ModelCapabilityProfile> GetByRestrictionLevel(ProviderRestrictionLevel level)
        => _profiles
            .Where(profile => profile.RestrictionLevel == level)
            .ToList();
}
