using System.Xml.Linq;
using System.Diagnostics;
using CognitivePlatform.Admin.Services.Testing;

namespace CognitivePlatform.Tests;

public sealed class AdminPublishTests
{
    [Fact]
    public async Task Publish_Preserves_Admin_And_Direct_Api_Settings_And_Still_Rejects_Unrelated_Collisions()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"cp-publish-{Guid.NewGuid():N}");
        var api = Path.Combine(directory, "CognitivePlatform");
        var admin = Path.Combine(directory, "CognitivePlatform.Admin");
        Directory.CreateDirectory(api);
        Directory.CreateDirectory(admin);
        try
        {
            var actual = XDocument.Load(Path.Combine(AdminLifecycleSafetyTests.RepositoryRoot, "CognitivePlatform", "CognitivePlatform.Admin", "CognitivePlatform.Admin.csproj"));
            var filter = actual.Descendants("Target").Single(element => (string?)element.Attribute("Name") == "ExcludeReferencedApiSettingsFromAdminPublish");
            var apiProject = Path.Combine(api, "FixtureApi.csproj");
            var adminProject = Path.Combine(admin, "FixtureAdmin.csproj");
            await File.WriteAllTextAsync(apiProject, "<Project Sdk=\"Microsoft.NET.Sdk.Web\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Library</OutputType></PropertyGroup></Project>");
            await File.WriteAllTextAsync(adminProject, "<Project Sdk=\"Microsoft.NET.Sdk.Web\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup><ProjectReference Include=\"../CognitivePlatform/FixtureApi.csproj\" /></ItemGroup>" + filter + "</Project>");
            await File.WriteAllTextAsync(Path.Combine(admin, "Program.cs"), "class Program { static void Main() {} }");
            foreach (var name in new[] { "appsettings.json", "appsettings.Production.json", "appsettings.QA.json" })
            {
                await File.WriteAllTextAsync(Path.Combine(api, name), "{\"owner\":\"api\"}");
                await File.WriteAllTextAsync(Path.Combine(admin, name), "{\"owner\":\"admin\"}");
            }
            async Task<(int? ExitCode, string Log)> Publish(string project, string output)
            {
                var lines = new System.Collections.Concurrent.ConcurrentQueue<string>();
                var start = new ProcessStartInfo("dotnet") { WorkingDirectory = directory };
                foreach (var argument in new[] { "publish", project, "-o", output, "--verbosity", "quiet" }) start.ArgumentList.Add(argument);
                var result = await new TestProcessRunner().RunAsync(start, lines.Enqueue, TimeSpan.FromSeconds(60), CancellationToken.None);
                return (result.ExitCode, string.Join(Environment.NewLine, lines));
            }
            var adminOutput = Path.Combine(directory, "admin-output");
            var apiOutput = Path.Combine(directory, "api-output");
            var adminPublished = await Publish(adminProject, adminOutput);
            Assert.True(adminPublished.ExitCode == 0, adminPublished.Log);
            var apiPublished = await Publish(apiProject, apiOutput);
            Assert.True(apiPublished.ExitCode == 0, apiPublished.Log);
            foreach (var name in new[] { "appsettings.json", "appsettings.Production.json", "appsettings.QA.json" })
            {
                Assert.Equal("{\"owner\":\"admin\"}", await File.ReadAllTextAsync(Path.Combine(adminOutput, name)));
                Assert.Equal("{\"owner\":\"api\"}", await File.ReadAllTextAsync(Path.Combine(apiOutput, name)));
            }
            // A second, unrelated relative-path conflict must remain an SDK error.
            await File.WriteAllTextAsync(Path.Combine(api, "unrelated.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(admin, "unrelated.json"), "{}");
            var collision = await Publish(adminProject, adminOutput);
            Assert.NotEqual(0, collision.ExitCode);
            Assert.Contains("NETSDK1152", collision.Log);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void Admin_Publish_Excludes_Only_Referenced_Api_Settings_Without_Disabling_Conflict_Checks()
    {
        var project = XDocument.Load(Path.Combine(AdminLifecycleSafetyTests.RepositoryRoot, "CognitivePlatform", "CognitivePlatform.Admin", "CognitivePlatform.Admin.csproj"));
        Assert.DoesNotContain(project.Descendants(), element => element.Name.LocalName == "ErrorOnDuplicatePublishOutputFiles" && element.Value == "false");
        var target = Assert.Single(project.Descendants("Target"), element => (string?)element.Attribute("Name") == "ExcludeReferencedApiSettingsFromAdminPublish");
        Assert.Equal("_HandleFileConflictsForPublish", (string?)target.Attribute("BeforeTargets"));
        var remove = Assert.Single(target.Descendants("ResolvedFileToPublish"));
        Assert.Equal("$([System.IO.Path]::GetFullPath('$(MSBuildProjectDirectory)\u005c..\u005cCognitivePlatform'))\u005cappsettings*.json", (string?)remove.Attribute("Remove"));
    }
}
