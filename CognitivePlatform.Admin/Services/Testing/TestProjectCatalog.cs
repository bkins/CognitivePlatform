using System.Xml.Linq;

namespace CognitivePlatform.Admin.Services.Testing;

public sealed class TestProjectCatalog(TestOperationsOptions options)
{
    public IReadOnlyList<TestProfile> Profiles =>
    [
        new("unit", "Unit Tests", ["CognitivePlatform", "LocalAIAssistant", "CP.Shared.Primitives", "CP.Client.Core", "ConsoleUtilities", "ConfigurationDiagnostics", "WatchList"])
      , new("client-contracts", "Headless LAA client contracts", ["CP/ValidationPlatform/src/ValidationPlatform.Tests.LaaSmoke"], "FullyQualifiedName!~ValidationPlatform.Tests.LaaSmoke.SmokeTests")
    ];

    public IReadOnlyList<string> Discover(string profile)
    {
        var selected = Profiles.SingleOrDefault(item => item.Id == profile)
                    ?? throw new ArgumentException("Unknown test profile.", nameof(profile));
        var projects = new List<string>();
        foreach (var relative in selected.Roots)
        {
            var root = Path.Combine(options.RepositoryRoot, relative);
            if (!Directory.Exists(root)) continue;
            foreach (var path in EnumerateProjects(root))
            {
                var name = Path.GetFileNameWithoutExtension(path);
                if (profile == "unit" && new[] { "Integration", "Smoke", "UI.Windows" }.Any(token => name.Contains(token, StringComparison.OrdinalIgnoreCase))) continue;
                if (new[] { ".Maui", "DemoSetup", "Seeder", "ValidationPlatform.Runner" }.Any(token => name.Contains(token, StringComparison.OrdinalIgnoreCase))) continue;
                var document = XDocument.Load(path);
                var explicitTest = document.Descendants().FirstOrDefault(element => element.Name.LocalName == "IsTestProject")?.Value;
                if (string.Equals(explicitTest, "false", StringComparison.OrdinalIgnoreCase)) continue;
                var hasTestSdk = document.Descendants().Any(element => element.Name.LocalName == "PackageReference"
                    && string.Equals((string?)element.Attribute("Include"), "Microsoft.NET.Test.Sdk", StringComparison.OrdinalIgnoreCase));
                if (string.Equals(explicitTest, "true", StringComparison.OrdinalIgnoreCase) || hasTestSdk) projects.Add(Path.GetFullPath(path));
            }
        }
        return projects.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IEnumerable<string> EnumerateProjects(string root)
    {
        foreach (var path in Directory.EnumerateFiles(root, "*.csproj")) yield return path;
        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            if (new[] { "bin", "obj", ".git", ".vs", "test-output" }.Contains(Path.GetFileName(directory), StringComparer.OrdinalIgnoreCase)) continue;
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) continue;
            foreach (var path in EnumerateProjects(directory)) yield return path;
        }
    }
}
