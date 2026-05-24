using Microsoft.Extensions.Options;
using Moq;
using CognitivePlatform.Api.Domains.Personas;
using CognitivePlatform.Api.Domains.Personas.Models;

namespace CognitivePlatform.Tests;

public class ModelCapabilityRegistryTests
{
    private static ModelCapabilityRegistry BuildRegistry(IEnumerable<ModelCapabilityProfile> profiles)
    {
        var options = new Mock<IOptions<List<ModelCapabilityProfile>>>();
        options.Setup(opt => opt.Value).Returns(profiles.ToList());
        return new ModelCapabilityRegistry(options.Object);
    }

    [Fact]
    public void GetAll_ReturnsAllConfiguredProfiles()
    {
        var profiles = new[]
        {
            new ModelCapabilityProfile { ModelName = "model-a", RestrictionLevel = ProviderRestrictionLevel.Permissive }
          , new ModelCapabilityProfile { ModelName = "model-b", RestrictionLevel = ProviderRestrictionLevel.Strict     }
        };
        var registry = BuildRegistry(profiles);

        var result = registry.GetAll();

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void GetByModel_ReturnsMatchingProfile()
    {
        var profiles = new[]
        {
            new ModelCapabilityProfile { ModelName = "llama-3.3-70b", RestrictionLevel = ProviderRestrictionLevel.Permissive }
          , new ModelCapabilityProfile { ModelName = "gpt-4o-mini",   RestrictionLevel = ProviderRestrictionLevel.Strict     }
        };
        var registry = BuildRegistry(profiles);

        var result = registry.GetByModel("llama-3.3-70b");

        Assert.NotNull(result);
        Assert.Equal("llama-3.3-70b",                result.ModelName);
        Assert.Equal(ProviderRestrictionLevel.Permissive, result.RestrictionLevel);
    }

    [Fact]
    public void GetByModel_ReturnsNull_WhenModelNotFound()
    {
        var profiles = new[]
        {
            new ModelCapabilityProfile { ModelName = "llama-3.3-70b", RestrictionLevel = ProviderRestrictionLevel.Permissive }
        };
        var registry = BuildRegistry(profiles);

        var result = registry.GetByModel("unknown-model");

        Assert.Null(result);
    }

    [Fact]
    public void GetByRestrictionLevel_FiltersCorrectly()
    {
        var profiles = new[]
        {
            new ModelCapabilityProfile { ModelName = "model-a", RestrictionLevel = ProviderRestrictionLevel.Permissive }
          , new ModelCapabilityProfile { ModelName = "model-b", RestrictionLevel = ProviderRestrictionLevel.Strict     }
          , new ModelCapabilityProfile { ModelName = "model-c", RestrictionLevel = ProviderRestrictionLevel.Permissive }
        };
        var registry = BuildRegistry(profiles);

        var permissive = registry.GetByRestrictionLevel(ProviderRestrictionLevel.Permissive);
        var strict     = registry.GetByRestrictionLevel(ProviderRestrictionLevel.Strict);

        Assert.Equal(2, permissive.Count);
        Assert.Single(strict);
        Assert.All(permissive, profile => Assert.Equal(ProviderRestrictionLevel.Permissive, profile.RestrictionLevel));
    }
}
