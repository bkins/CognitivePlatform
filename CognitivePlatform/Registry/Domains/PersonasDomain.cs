namespace CognitivePlatform.Api.Registry.Domains;

public sealed record PersonasDomain : IDomainDefinition
{
    public string Name        => "Personas";
    public string Description => "Named persona definition and persistent memory management.";

    public IReadOnlyList<string> Keywords => new[]
    {
        "persona"
      , "personas"
      , "synthetic person"
      , "create persona"
      , "persona memory"
    };
}
