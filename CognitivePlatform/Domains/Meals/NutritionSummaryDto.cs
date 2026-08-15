using System;

namespace CognitivePlatform.Api.Domains.Meals;

public sealed record NutritionSummaryDto
{
    public DateTimeOffset FromDateUtc            { get; init; }
    public DateTimeOffset ToDateUtc              { get; init; }
    public int            TotalMeals             { get; init; }
    public int            TotalFoodItems         { get; init; }
    public int            EnrichedFoodItemsCount { get; init; }
    public double         TotalCalories          { get; init; }
    public double         TotalProteinGrams      { get; init; }
    public double         TotalCarbsGrams        { get; init; }
    public double         TotalFatGrams          { get; init; }
    public double         TotalFiberGrams        { get; init; }
}
