namespace CognitivePlatform.Api.Registry.Domains;

public sealed record HealthDomain : IDomainDefinition
{
    public string Name        => "Health";
    public string Description => "Health and fitness data from your device.";

    public IReadOnlyList<string> Keywords => new[]
    {
        "health"
      , "steps"
      , "sleep"
      , "heart rate"
      , "distance"
      , "fitness"
      , "calories"
      , "bpm"
      , "activity"
      , "walk"
      , "walked"
      , "run"
      , "exercise"
    };
}
