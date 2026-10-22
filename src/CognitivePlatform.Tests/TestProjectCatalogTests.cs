using CognitivePlatform.Admin.Services.Testing;

namespace CognitivePlatform.Tests;

public sealed class TestProjectCatalogTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cp-catalog-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Discovery_Requires_Actual_Test_Metadata_And_Excludes_App_And_Generated_Trees()
    {
        Write("CognitivePlatform/Valid.Tests.csproj", "<Project><ItemGroup><PackageReference Include=\"Microsoft.NET.Test.Sdk\" /></ItemGroup></Project>");
        Write("CognitivePlatform/Explicit.Tests.csproj", "<Project><PropertyGroup><IsTestProject>true</IsTestProject></PropertyGroup></Project>");
        Write("LocalAIAssistant/LocalAIAssistant.Ui.Maui.csproj", "<Project><ItemGroup><PackageReference Include=\"xunit.assert\" /></ItemGroup></Project>");
        Write("CognitivePlatform/Comment.Tests.csproj", "<Project><!-- xunit --></Project>");
        Write("CognitivePlatform/bin/Fake.Tests.csproj", "<Project><PropertyGroup><IsTestProject>true</IsTestProject></PropertyGroup></Project>");
        Write("CognitivePlatform/TestHarness.csproj", "<Project/>");
        var catalog = new TestProjectCatalog(new TestOperationsOptions { RepositoryRoot = _root });

        var projects = catalog.Discover("unit");

        Assert.Equal(2, projects.Count);
        Assert.All(projects, project => Assert.Contains("Tests.csproj", project));
    }

    [Fact]
    public void Discovery_Rejects_Unknown_Profile()
    {
        var catalog = new TestProjectCatalog(new TestOperationsOptions { RepositoryRoot = _root });

        Assert.Throws<ArgumentException>(() => catalog.Discover("shell"));
    }

    private void Write(string relative, string text)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
