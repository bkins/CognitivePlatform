namespace CognitivePlatform.Api.Registry.Domains;

public sealed record SystemDomain : IDomainDefinition
{
    public string Name        => "System";
    public string Description => "Platform infrastructure, system diagnostics, LLM configuration, and interpreter meta-actions.";

    public IReadOnlyList<string> Keywords => new[]
    {
        "help"
      , "version"
      , "health"
      , "model"
      , "provider"
      , "capabilities"
      , "actions"
      , "settings"
      , "what can you do"
      , "list actions"
    };
}
