namespace CognitivePlatform.Api.Registry.Domains;

public sealed record JournalDomain : IDomainDefinition
{
    public string Name        => "Journal";
    public string Description => "Append-only journal entries with mood tracking and revision history.";

    public IReadOnlyList<string> Keywords => new[]
    {
        "journal"
      , "entry"
      , "entries"
      , "mood"
      , "diary"
      , "wrote"
      , "journaled"
      , "reflection"
      , "revision"
    };
}
