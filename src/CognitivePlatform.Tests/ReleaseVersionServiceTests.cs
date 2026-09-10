using CognitivePlatform.Admin.Services;
using Microsoft.Extensions.Configuration;

namespace CognitivePlatform.Tests;

public sealed class ReleaseVersionServiceTests : IDisposable
{
    private readonly string _versionJsonPath = Path.Combine(Path.GetTempPath(), $"release-version-{Guid.NewGuid():N}.json");

    [Fact]
    public void IncrementBuild_WhenVersionFileExists_IncrementsAndPersistsTheBuild()
    {
        File.WriteAllText(_versionJsonPath, "{ \"Major\": 1, \"Minor\": 3, \"Patch\": 0, \"Build\": 87 }");
        var service = CreateService();

        var version = service.IncrementBuild();

        Assert.Equal("1.3.0.88", version);
        Assert.Contains("\"Build\": 88", File.ReadAllText(_versionJsonPath));
    }

    [Fact]
    public void GetCurrentVersion_WhenVersionFileExists_DoesNotIncrementTheBuild()
    {
        File.WriteAllText(_versionJsonPath, "{ \"Major\": 1, \"Minor\": 3, \"Patch\": 0, \"Build\": 87 }");
        var service = CreateService();

        var version = service.GetCurrentVersion();

        Assert.Equal("1.3.0.87", version);
        Assert.Contains("\"Build\": 87", File.ReadAllText(_versionJsonPath));
    }

    public void Dispose()
    {
        if (File.Exists(_versionJsonPath)) File.Delete(_versionJsonPath);
    }

    private ReleaseVersionService CreateService()
    {
        var configuration = new ConfigurationBuilder()
                            .AddInMemoryCollection(new Dictionary<string, string?>
                            {
                                ["SystemPaths:VersionJsonPath"] = _versionJsonPath
                            })
                            .Build();
        return new ReleaseVersionService(configuration);
    }
}
