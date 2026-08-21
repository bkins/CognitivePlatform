namespace CognitivePlatform.Api.Registry.Domains;

public sealed record FileSyncDomain : IDomainDefinition
{
    public string Name        => "FileSync";
    public string Description => "Cross-device file synchronisation managed through natural language commands.";

    public IReadOnlyList<string> Keywords => new[]
    {
        "file"
      , "files"
      , "sync"
      , "synchronise"
      , "synchronize"
      , "folder"
      , "copy"
      , "transfer"
      , "backup"
      , "document"
      , "documents"
      , "download"
      , "upload"
      , "in sync"
      , "phone files"
    };
}
