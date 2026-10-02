using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CognitivePlatform.Api.Companion;
using CognitivePlatform.Api.Controllers;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Data.Sqlite;

var root = Path.GetFullPath(args.Single());
if (Directory.Exists(root)) throw new InvalidOperationException("Use a fresh fixture directory.");
var workspace = Path.Combine(root, "workspace");
Directory.CreateDirectory(Path.Combine(workspace, "notes"));
await File.WriteAllTextAsync(Path.Combine(workspace, "one.md"), "<!-- axiom:documentId=bc4d0775-8ad8-4ab6-ae2e-e864f63b8791 -->\n# Cedar fixture\n\nFirst release: **Friday**.\n\n## Checklist\n- [x] Read only\n- [ ] Human phone acceptance\n\n[Unsafe](javascript:alert)\n<script>window.fixtureExecuted=true</script>\n");
await File.WriteAllTextAsync(Path.Combine(workspace, "notes", "two.md"), "# Café 🌲\n\nSecond fixture schedules Cedar for Monday.\n\n| Item | Value |\n| --- | --- |\n| Budget | 100 |\n");
var metadataPath = Path.Combine(root, "synthetic-metadata.db");
await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = metadataPath, Pooling = false }.ToString()))
{
    await connection.OpenAsync();
    await using var command = connection.CreateCommand();
    command.CommandText = "CREATE TABLE SchemaInfo(Version INTEGER); INSERT INTO SchemaInfo VALUES(9); CREATE TABLE DocumentMetadata(DocumentId TEXT,FilePath TEXT); CREATE TABLE DocumentClassification(DocumentId TEXT,TagsJson TEXT,Type TEXT,AliasesJson TEXT,Revision INTEGER); CREATE TABLE DocumentRelationships(SourceId TEXT,TargetId TEXT,Kind TEXT,Revision INTEGER); INSERT INTO DocumentMetadata VALUES('bc4d0775-8ad8-4ab6-ae2e-e864f63b8791',$path); INSERT INTO DocumentClassification VALUES('bc4d0775-8ad8-4ab6-ae2e-e864f63b8791','[\"Cedar\",\"<script>fixtureExecuted=true</script>\"]','Project','[\"Tree\"]',2); INSERT INTO DocumentRelationships VALUES('bc4d0775-8ad8-4ab6-ae2e-e864f63b8791','770bdb92-7436-49eb-98cc-0227d9b15f78','references',3);";
    command.Parameters.AddWithValue("$path", Path.Combine(workspace, "one.md"));
    await command.ExecuteNonQueryAsync();
}
var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
builder.Logging.ClearProviders();
builder.WebHost.UseUrls("http://127.0.0.1:0");
builder.Services.Configure<CompanionSettings>(settings =>
{
    settings.Enabled = true;
    settings.ClientKeySha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("fixture-only-companion-credential-at-least-32")));
    settings.Workspaces = [new CompanionWorkspace { Id = "synthetic", Label = "Synthetic Cedar workspace", RootPath = workspace, MetadataDatabasePath = metadataPath }];
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
