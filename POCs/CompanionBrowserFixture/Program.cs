using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CognitivePlatform.Api.Companion;
using CognitivePlatform.Api.Controllers;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

var root = Path.GetFullPath(args.Single());
if (Directory.Exists(root)) throw new InvalidOperationException("Use a fresh fixture directory.");
var workspace = Path.Combine(root, "workspace");
Directory.CreateDirectory(Path.Combine(workspace, "notes"));
await File.WriteAllTextAsync(Path.Combine(workspace, "one.md"), "<!-- axiom:documentId=bc4d0775-8ad8-4ab6-ae2e-e864f63b8791 -->\n# Cedar fixture\n\nFirst release: **Friday**.\n\n## Checklist\n- [x] Read only\n- [ ] Human phone acceptance\n\n[Unsafe](javascript:alert)\n<script>window.fixtureExecuted=true</script>\n");
await File.WriteAllTextAsync(Path.Combine(workspace, "notes", "two.md"), "# Café 🌲\n\nSecond fixture schedules Cedar for Monday.\n\n| Item | Value |\n| --- | --- |\n| Budget | 100 |\n");
var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
builder.Logging.ClearProviders();
builder.WebHost.UseUrls("http://127.0.0.1:0");
builder.Services.Configure<CompanionSettings>(settings =>
{
    settings.Enabled = true;
    settings.ClientKeySha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("fixture-only-companion-credential-at-least-32")));
    settings.Workspaces = [new CompanionWorkspace { Id = "synthetic", Label = "Synthetic Cedar workspace", RootPath = workspace }];
});
builder.Services.AddSingleton<CompanionWorkspaceReader>();
builder.Services.AddSingleton<CompanionRequestGate>();
builder.Services.AddControllers().AddApplicationPart(typeof(CompanionController).Assembly);
var host = builder.Build();
host.MapControllers();
await host.StartAsync();
var endpoint = host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
await File.WriteAllTextAsync(Path.Combine(root, "launch.json"), JsonSerializer.Serialize(new { endpoint, syntheticDataOnly = true, userDataRegistered = false }));
Console.WriteLine("Isolated companion browser fixture ready; endpoint saved without credential.");
await host.WaitForShutdownAsync();
