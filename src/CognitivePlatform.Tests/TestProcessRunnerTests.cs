using System.Diagnostics;
using CognitivePlatform.Admin.Services.Testing;

namespace CognitivePlatform.Tests;

public sealed class TestProcessRunnerTests
{
    [Fact]
    public async Task Timeout_Kills_Only_Its_Own_Child_While_Unrelated_Process_Survives()
    {
        var start = new ProcessStartInfo("pwsh") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-Command", "Start-Sleep -Seconds 30" }) start.ArgumentList.Add(argument);
        using var unrelated = Process.Start(start)!;
        try
        {
            var result = await new TestProcessRunner().RunAsync(start, _ => { }, TimeSpan.FromMilliseconds(300), CancellationToken.None);

            Assert.True(result.TimedOut);
            Assert.False(unrelated.HasExited);
        }
        finally
        {
            if (!unrelated.HasExited) unrelated.Kill(true);
            await unrelated.WaitForExitAsync();
        }
    }

    [Fact]
    public void Api_Build_Has_No_ImageName_Termination_Target()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CognitivePlatform", "CognitivePlatform", "CognitivePlatform.Api.csproj"))) directory = directory.Parent;
        Assert.NotNull(directory);
        var project = System.Xml.Linq.XDocument.Load(Path.Combine(directory.FullName, "CognitivePlatform", "CognitivePlatform", "CognitivePlatform.Api.csproj"));

        Assert.DoesNotContain(project.Descendants().Where(element => element.Name.LocalName == "Exec"), element => (string?)element.Attribute("Command") is { } command && command.Contains("taskkill", StringComparison.OrdinalIgnoreCase) && command.Contains("/im", StringComparison.OrdinalIgnoreCase));
    }
}
