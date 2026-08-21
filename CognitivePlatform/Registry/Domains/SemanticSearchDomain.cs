namespace CognitivePlatform.Api.Registry.Domains;

public sealed record SemanticSearchDomain : IDomainDefinition
{
    public string Name        => "Search";
    public string Description => "Semantic search across all domain content using vector embeddings.";

    public IReadOnlyList<string> Keywords => new[]
    {
        "semantic"
      , "search"
      , "find"
      , "similar"
      , "related"
      , "about"
      , "written"
      , "notes"
    };
}
