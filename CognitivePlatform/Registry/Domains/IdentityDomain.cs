namespace CognitivePlatform.Api.Registry.Domains;

public sealed record IdentityDomain : IDomainDefinition
{
    public string Name        => "Identity";
    public string Description => "User identity profile, behavioral assertions, and derived insight management.";

    public IReadOnlyList<string> Keywords => new[]
    {
        // Domain-level terms
        "identity"
      , "profile"
      , "who am i"
      , "about me"
      , "assertion"
      , "snapshot"
      , "behavioral"

        // Profile list-field phrases
      , "personality traits"
      , "core values"
      , "leadership"
      , "long-term goals"
      , "long term goals"
      , "communication preferences"

        // Shorter aliases
      , "my traits"
      , "my values"
      , "my goals"
      , "strengths"
      , "stressors"
      , "occupation"
    };
}
