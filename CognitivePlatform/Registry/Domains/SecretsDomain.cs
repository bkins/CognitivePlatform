namespace CognitivePlatform.Api.Registry.Domains;

public sealed record SecretsDomain : IDomainDefinition
{
    public string Name        => "Secrets";
    public string Description => "Encrypted secrets vault storing sensitive credentials and private data.";

    public IReadOnlyList<string> Keywords => new[]
    {
        "secret"
      , "secrets"
      , "vault"
      , "password"
      , "passwords"
      , "credential"
      , "credentials"
      , "private"
      , "encrypt"
      , "encrypted"
      , "sensitive"
    };
}
