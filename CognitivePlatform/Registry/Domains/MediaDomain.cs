namespace CognitivePlatform.Api.Registry.Domains;

public sealed record MediaDomain : IDomainDefinition
{
    public string Name        => "Media";
    public string Description => "Media attachment management: upload, list, and retrieve files attached to any knowledge item.";

    public IReadOnlyList<string> Keywords => new[]
    {
        "media"
      , "attachment"
      , "attachments"
      , "photo"
      , "image"
      , "file attached"
      , "upload"
      , "attached files"
    };
}
