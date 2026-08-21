namespace CognitivePlatform.Api.Registry.Domains;

public sealed record KnowledgeDomain : IDomainDefinition
{
    public string Name        => "Knowledge";
    public string Description => "Knowledge pattern analysis and cross-domain insight surfacing.";

    public IReadOnlyList<string> Keywords => new[]
    {
        "insight"
      , "insights"
      , "knowledge"
      , "inbox"
      , "pattern"
      , "patterns"
    };
}
