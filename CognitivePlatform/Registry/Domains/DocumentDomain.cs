namespace CognitivePlatform.Api.Registry.Domains;

public sealed record DocumentDomain : IDomainDefinition
{
    public string Name        => "Document";
    public string Description => "Indexing and semantic search over arbitrary files on disk.";

    public IReadOnlyList<string> Keywords => new[]
    {
        "document"
      , "documents"
      , "file"
      , "files"
      , "index"
      , "indexed"
      , "pdf"
      , "markdown"
      , "text file"
    };
}
