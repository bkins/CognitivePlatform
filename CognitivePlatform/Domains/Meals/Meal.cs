using System;
using System.Collections.Generic;

namespace CognitivePlatform.Api.Domains.Meals;

public sealed class Meal
{
    public string          Id          { get; init; } = Guid.NewGuid().ToString("N");
    public MealType        MealType    { get; init; } = MealType.Unspecified;
    public DateTimeOffset  ConsumedAt  { get; set; } = DateTimeOffset.UtcNow;
    public List<FoodEntry> Foods       { get; init; } = new();
    public string?         Notes       { get; init; }
    public string          Source      { get; init; } = "NaturalLanguage";
    public bool            IsDeleted   { get; init; }
    public DateTime?       DeletedUtc  { get; init; }
}
