namespace CognitivePlatform.Api.Registry.Domains;

public sealed record WellbeingDomain : IDomainDefinition
{
    public string Name        => "Wellbeing";
    public string Description => "Cross-domain wellbeing pattern analysis combining sleep, activity, tasks, and journal signals.";

    public IReadOnlyList<string> Keywords => new[]
    {
        "wellbeing"
      , "well-being"
      , "wellness"
      , "patterns"
      , "trends"
      , "sleep health"
      , "energy"
      , "burnout"
      , "balance"
      , "correlation"
      , "health check"
    };
}
