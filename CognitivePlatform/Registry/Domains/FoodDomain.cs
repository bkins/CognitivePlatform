namespace CognitivePlatform.Api.Registry.Domains;

public sealed record FoodDomain : IDomainDefinition
{
    public string Name        => "Food";
    public string Description => "Natural-language food logging and nutrition tracking.";

    public IReadOnlyList<string> Keywords => new[]
    {
        "food"
      , "meal"
      , "meals"
      , "eat"
      , "ate"
      , "breakfast"
      , "lunch"
      , "dinner"
      , "snack"
      , "nutrition"
      , "calories"
      , "pizza"
      , "egg"
      , "bacon"
      , "coffee"
      , "banana"
      , "oatmeal"
      , "salmon"
      , "asparagus"
    };
}
