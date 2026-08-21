namespace CognitivePlatform.Api.Registry.Domains;

public sealed record PersonalityDomain : IDomainDefinition
{
    public string Name        => "Personality";
    public string Description => "Response personality and tone configuration for the assistant.";

    public IReadOnlyList<string> Keywords => new[]
    {
        "personality"
      , "tone"
      , "style"
      , "friendly"
      , "witty"
      , "zen"
      , "motivational"
      , "assistant style"
    };
}
