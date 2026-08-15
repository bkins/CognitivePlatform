using System;
using System.Collections.Generic;
using CognitivePlatform.Api.COCE;
using CognitivePlatform.Api.Domains.Meals;
using Xunit;

namespace CognitivePlatform.Tests;

public class ObjectConstructionEngineTests
{
    private readonly MealValidator           _validator = new();
    private readonly ObjectConstructionEngine _engine;

    public ObjectConstructionEngineTests()
    {
        _engine = new ObjectConstructionEngine(new IObjectValidator[] { _validator });
    }

    [Fact]
    public void Construct_ValidMealJson_ReturnsDeserializedMeal()
    {
        var json = """
                   {
                       "mealType": "Breakfast",
                       "consumedAt": "2026-08-14T08:30:00",
                       "foods": [
                           { "name": "Oatmeal", "quantity": 1, "unit": "bowl" }
                       ]
                   }
                   """;

        var result = _engine.Construct(json, typeof(Meal));

        var meal = Assert.IsType<Meal>(result);
        Assert.Equal(MealType.Breakfast, meal.MealType);
        Assert.Single(meal.Foods);
        Assert.Equal("Oatmeal", meal.Foods[0].Name);
    }

    [Fact]
    public void Construct_EmptyJson_ReturnsNull()
    {
        var result = _engine.Construct("", typeof(Meal));

        Assert.Null(result);
    }

    [Fact]
    public void Construct_InvalidMeal_ThrowsException_WhenFoodsListIsEmpty()
    {
        var json = """
                   {
                       "mealType": "Breakfast",
                       "foods": []
                   }
                   """;

        var ex = Assert.Throws<InvalidOperationException>(() => _engine.Construct(json, typeof(Meal)));

        Assert.Contains("COCE validation failed", ex.Message);
        Assert.Contains("at least one food item", ex.Message);
    }

    [Fact]
    public void Construct_InvalidMeal_ThrowsException_WhenFoodItemNameIsWhitespace()
    {
        var json = """
                   {
                       "mealType": "Dinner",
                       "foods": [
                           { "name": "   " }
                       ]
                   }
                   """;

        var ex = Assert.Throws<InvalidOperationException>(() => _engine.Construct(json, typeof(Meal)));

        Assert.Contains("missing a valid name", ex.Message);
    }

    [Fact]
    public void TryConstruct_ValidMeal_ReturnsTrue_WithSuccessfulValidation()
    {
        var json = """
                   {
                       "mealType": "Lunch",
                       "foods": [
                           { "name": "Salad", "quantity": 1 }
                       ]
                   }
                   """;

        var success = _engine.TryConstruct(json, typeof(Meal), out var result, out var validation);

        Assert.True(success);
        Assert.NotNull(result);
        Assert.True(validation.IsValid);
        Assert.Empty(validation.Errors);
    }

    [Fact]
    public void TryConstruct_InvalidMeal_ReturnsFalse_WithMissingPropertiesDiagnostic()
    {
        var json = """
                   {
                       "mealType": "Lunch",
                       "foods": []
                   }
                   """;

        var success = _engine.TryConstruct(json, typeof(Meal), out var result, out var validation);

        Assert.False(success);
        Assert.False(validation.IsValid);
        Assert.Contains(nameof(Meal.Foods), validation.MissingProperties);
    }

    [Fact]
    public void IncrementalObjectBuilder_ProgressivelyMergesProperties_AcrossTurns()
    {
        var builder = new IncrementalObjectBuilder(_engine, new IObjectValidator[] { _validator });
        var sessionId = Guid.NewGuid().ToString("N");

        builder.GetOrCreateSession(sessionId, typeof(Meal));

        // Turn 1: Partial meal type only — incomplete
        var turn1Json = """{ "mealType": "Breakfast" }""";
        var turn1Valid = builder.ApplyIncrementalUpdate(sessionId, turn1Json, out var obj1, out var val1);

        Assert.False(turn1Valid);
        Assert.False(val1.IsValid);
        Assert.Contains("at least one food item", val1.Errors[0]);

        // Turn 2: Provide foods array — becomes complete and valid
        var turn2Json = """{ "foods": [{ "name": "Scrambled Eggs", "quantity": 2 }] }""";
        var turn2Valid = builder.ApplyIncrementalUpdate(sessionId, turn2Json, out var obj2, out var val2);

        Assert.True(turn2Valid);
        Assert.True(val2.IsValid);
        Assert.NotNull(obj2);

        var completedMeal = Assert.IsType<Meal>(obj2);
        Assert.Equal(MealType.Breakfast, completedMeal.MealType);
        Assert.Single(completedMeal.Foods);
        Assert.Equal("Scrambled Eggs", completedMeal.Foods[0].Name);

        // Turn 3: Add coffee addition to foods
        var turn3Json = """{ "foods": [{ "name": "Coffee" }] }""";
        var turn3Valid = builder.ApplyIncrementalUpdate(sessionId, turn3Json, out var obj3, out var val3);

        Assert.True(turn3Valid);
        var finalMeal = Assert.IsType<Meal>(obj3);
        Assert.Equal(2, finalMeal.Foods.Count);
    }

    [Fact]
    public void IncrementalObjectBuilder_TryGetCompletedObject_ReturnsFalse_WhenValidationIncomplete()
    {
        var builder = new IncrementalObjectBuilder(_engine, new IObjectValidator[] { _validator });
        var sessionId = Guid.NewGuid().ToString("N");

        builder.GetOrCreateSession(sessionId, typeof(Meal));
        builder.ApplyIncrementalUpdate(sessionId, """{ "mealType": "Dinner" }""", out _, out _);

        var hasCompleted = builder.TryGetCompletedObject(sessionId, out var completed);

        Assert.False(hasCompleted);
        Assert.Null(completed);
    }
}
