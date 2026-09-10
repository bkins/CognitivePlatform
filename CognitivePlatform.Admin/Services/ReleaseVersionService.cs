using System.Text.Json;

namespace CognitivePlatform.Admin.Services;

public sealed class ReleaseVersionService : IReleaseVersionService
{
    private static readonly object VersionLock = new();

    private readonly string _versionJsonPath;

    public ReleaseVersionService(IConfiguration configuration)
    {
        _versionJsonPath = configuration["SystemPaths:VersionJsonPath"]
                           ?? @"C:\Users\benho\source\repos\CP\CP.Workbench\version.json";
    }

    public string GetCurrentVersion()
    {
        lock (VersionLock)
        {
            return ReadVersion(incrementBuild: false);
        }
    }

    public string IncrementBuild()
    {
        lock (VersionLock)
        {
            return ReadVersion(incrementBuild: true);
        }
    }

    private string ReadVersion(bool incrementBuild)
    {
        if (!File.Exists(_versionJsonPath)) return "1.0.0.1";

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(_versionJsonPath));

            var root  = document.RootElement;
            var major = root.GetProperty("Major").GetInt32();
            var minor = root.GetProperty("Minor").GetInt32();
            var patch = root.GetProperty("Patch").GetInt32();
            var build = root.GetProperty("Build").GetInt32();

            if (incrementBuild)
            {
                build++;
                var updatedVersion = new { Major = major, Minor = minor, Patch = patch, Build = build };
                File.WriteAllText(_versionJsonPath, JsonSerializer.Serialize(updatedVersion, new JsonSerializerOptions { WriteIndented = true }));
            }

            return $"{major}.{minor}.{patch}.{build}";
        }
        catch (JsonException)
        {
            return "1.0.0.1";
        }
        catch (IOException)
        {
            return "1.0.0.1";
        }
    }
}
