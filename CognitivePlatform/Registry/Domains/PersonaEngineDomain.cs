namespace CognitivePlatform.Api.Registry.Domains;

public sealed record PersonaEngineDomain : IDomainDefinition
{
    public string Name        => "PersonaEngine";
    public string Description => "Active persona management and context switching for the assistant engine.";

    public IReadOnlyList<string> Keywords => new[]
    {
        "active persona"
      , "persona context"
      , "switch persona"
      , "begin persona"
      , "end persona"
    };
}
