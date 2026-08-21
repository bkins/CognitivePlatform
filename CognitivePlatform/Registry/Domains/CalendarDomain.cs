namespace CognitivePlatform.Api.Registry.Domains;

public sealed record CalendarDomain : IDomainDefinition
{
    public string Name        => "Calendar";
    public string Description => "Calendar event management with multi-calendar support and free-time scheduling.";

    public IReadOnlyList<string> Keywords => new[]
    {
        "calendar"
      , "schedule"
      , "meeting"
      , "meetings"
      , "appointment"
      , "event"
      , "events"
      , "busy"
      , "free time"
      , "book"
      , "availability"
      , "on my calendar"
    };
}
