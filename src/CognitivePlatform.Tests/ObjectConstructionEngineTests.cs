using System;
using System.Collections.Generic;
using CognitivePlatform.Api.COCE;
using CognitivePlatform.Api.Domains.Meals;
using Xunit;

namespace CognitivePlatform.Tests;

public class ObjectConstructionEngineTests
{
    private readonly ObjectConstructionEngine _engine = new();

    [Fact]
    public void Construct_ReturnsNull_WhenJsonIsEmpty()
    {
        var result = _engine.Construct(string.Empty, typeof(Meal));

        Assert.Null(result);
    }

    [Fact]
    public void Construct_BuildsSimpleObject_WithPascalCaseMapping()
    {
        var json = """
                   {
                       "name": "Egg",
                       "quantity": 2,
                       "preparation": "Scrambled"
                   }
                   """;

        var result = _engine.Construct(json, typeof(FoodEntry)) as FoodEntry;

        Assert.NotNull(result);
        Assert.Equal("Egg",       result.Name);
        Assert.Equal(2.0,         result.Quantity);
        Assert.Equal("Scrambled", result.Preparation);
    }

    [Fact]
    public void Construct_BuildsNestedObjectGraph_AndCollections()
    {
        var json = """
                   {
                       "mealType": "Breakfast",
                       "notes": "Healthy breakfast",
                       "foods": [
                           {
                               "name": "Coffee",
                               "quantity": 1,
                               "additions": ["Cream", "Sugar"],
                               "nutrition": {
                                   "calories": 50,
                                   "proteinGrams": 1
                               }
                           }
                       ]
                   }
                   """;

        var result = _engine.Construct(json, typeof(Meal)) as Meal;

        Assert.NotNull(result);
        Assert.Equal(MealType.Breakfast, result.MealType);
        Assert.Equal("Healthy breakfast", result.Notes);
        Assert.Single(result.Foods);
        
        var food = result.Foods[0];
        Assert.Equal("Coffee", food.Name);
        Assert.Equal(1.0,      food.Quantity);
        Assert.Equal(new[] { "Cream", "Sugar" }, food.Additions);
        
        Assert.NotNull(food.Nutrition);
        Assert.Equal(50.0, food.Nutrition.Calories);
        Assert.Equal(1.0,  food.Nutrition.ProteinGrams);
    }

    [Fact]
    public void Construct_ThrowsInvalidOperationException_WhenJsonIsMalformed()
    {
        var json = "{ malformed json }";

        Assert.Throws<InvalidOperationException>(() => _engine.Construct(json, typeof(Meal)));
    }
}
