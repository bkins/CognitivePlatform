using System;
using System.Collections.Generic;

namespace CognitivePlatform.Api.Domains.Meals;

public sealed class FoodEntry
{
    public Guid            Id          { get; init; } = Guid.NewGuid();
    public string          Name        { get; init; } = string.Empty;
    public double?         Quantity    { get; init; }
    public string?         Unit        { get; init; }
    public string?         Preparation { get; init; }
    public string?         Brand       { get; init; }
    public List<string>    Additions   { get; init; } = new();
    public NutritionalInfo? Nutrition   { get; init; }
}
