namespace CognitivePlatform.Api.Registry.Domains;

public sealed record DailyRecordDomain : IDomainDefinition
{
    public string Name        => "Daily";
    public string Description => "Daily structured log with open/close lifecycle, checkpoint tracking, and task rollover.";

    public IReadOnlyList<string> Keywords => new[]
    {
        "daily"
      , "open day"
      , "close day"
      , "checkpoint"
      , "morning"
      , "evening"
      , "end of day"
      , "start of day"
      , "daily plan"
      , "rollover"
      , "open the day"
      , "close the day"
    };
}
