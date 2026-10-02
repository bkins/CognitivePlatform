using CognitivePlatform.Api.Companion;
using Microsoft.Data.Sqlite;

namespace CognitivePlatform.Tests;

public sealed class CompanionMetadataReaderTests
{
    [Fact]
    public async Task Reads_Only_Published_Path_Matched_Metadata_Without_Changing_Database()
    {
        var root = Path.Combine(Path.GetTempPath(), "CP-companion-metadata-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var database = Path.Combine(root, "state.db");
        var identity = Guid.NewGuid();
        var related = Guid.NewGuid();
        var source = Path.Combine(root, "one.md");
        await File.WriteAllTextAsync(source, "source");
        await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database, Pooling = false }.ToString()))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE SchemaInfo(Version INTEGER); INSERT INTO SchemaInfo VALUES(8); CREATE TABLE DocumentMetadata(DocumentId TEXT,FilePath TEXT); CREATE TABLE DocumentClassification(DocumentId TEXT,TagsJson TEXT,Type TEXT,AliasesJson TEXT,Revision INTEGER); CREATE TABLE DocumentRelationships(SourceId TEXT,TargetId TEXT,Kind TEXT,Revision INTEGER); INSERT INTO DocumentMetadata VALUES($id,$path); INSERT INTO DocumentClassification VALUES($id,'[\"Cedar\"]','Project','[\"Tree\"]',2); INSERT INTO DocumentRelationships VALUES($id,$related,'references',3);";
            command.Parameters.AddWithValue("$id", identity.ToString("D"));
            command.Parameters.AddWithValue("$related", related.ToString("D"));
            command.Parameters.AddWithValue("$path", source);
            await command.ExecuteNonQueryAsync();
        }
        var before = await File.ReadAllBytesAsync(database);
        var workspace = new CompanionWorkspace { Id = "fixture", RootPath = root, MetadataDatabasePath = database };
        var reader = new CompanionMetadataReader();

        var metadata = await reader.ReadAsync(workspace, "one.md", identity, CancellationToken.None);

        Assert.True(metadata.Available);
        Assert.Equal("Project", metadata.Type);
        Assert.Equal(["Cedar"], metadata.Tags);
        Assert.Equal(related, Assert.Single(metadata.Relationships).RelatedDocumentId);
        Assert.Equal(before, await File.ReadAllBytesAsync(database));
        Assert.False((await reader.ReadAsync(workspace, "other.md", identity, CancellationToken.None)).Available);
        Assert.False((await reader.ReadAsync(workspace, "one.md", Guid.NewGuid(), CancellationToken.None)).Available);
        workspace.MetadataDatabasePath = Path.Combine(root, "missing.db");
        Assert.False((await reader.ReadAsync(workspace, "one.md", identity, CancellationToken.None)).Available);
        Assert.False(File.Exists(workspace.MetadataDatabasePath));
    }
}
