namespace CognitivePlatform.Api.Registry.Domains;

public sealed record CognitionDomain : IDomainDefinition
{
    public string Name        => "Cognition";
    public string Description => "Cognitive pattern awareness and reflective journaling insights.";

    public IReadOnlyList<string> Keywords => new[]
    {
        "cognitive"
      , "distortion"
      , "thinking patterns"
      , "journaling patterns"
      , "all-or-nothing"
      , "catastrophising"
      , "reflection"
    };
}
