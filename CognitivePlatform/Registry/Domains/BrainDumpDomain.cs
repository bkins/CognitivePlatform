namespace CognitivePlatform.Api.Registry.Domains;

public sealed record BrainDumpDomain : IDomainDefinition
{
    public string Name        => "BrainDump";
    public string Description => "Guided therapeutic brain dump: structured mental unloading across 7 categories to reduce cognitive load.";

    public IReadOnlyList<string> Keywords => new[]
    {
        "brain dump"
      , "braindump"
      , "guided journal"
      , "guided brain dump"
      , "mental dump"
      , "unload"
      , "avoidance"
      , "procrastination"
      , "guided journaling"
      , "mental load"
    };
}
