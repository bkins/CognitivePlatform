using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CognitivePlatform.Api.Domains.Meals;
using CognitivePlatform.Api.Execution;
using Moq;
using Xunit;

namespace CognitivePlatform.Tests;

public class MealActionsTests
{
    private readonly Mock<IMealService> _mealServiceMock = new();
    private readonly MealActions        _actions;

    public MealActionsTests()
    {
        _actions = new MealActions(_mealServiceMock.Object);
    }

    [Fact]
    public async Task LogMeal_ReturnsSuccess_WithFormattedDetails()
    {
        var food = new FoodEntry
                   {
                       Name        = "Egg"
                     , Quantity    = 2
                     , Preparation = "Scrambled"
                   };
        var meal = new Meal
                   {
                       MealType   = MealType.Breakfast
                     , Foods      = new List<FoodEntry> { food }
                     , ConsumedAt = new DateTime(2026, 8, 4, 8, 0, 0, DateTimeKind.Local)
                   };

        _mealServiceMock.Setup(service => service.SaveAsync(meal))
                        .ReturnsAsync(meal);

        var result = await _actions.LogMeal(meal);

        Assert.True(result.Success);
        Assert.Contains("Logged Breakfast", result.Message);
        Assert.Contains("Egg",             result.Message);
    }

    [Fact]
    public async Task ListMeals_ReturnsFormattedLog_WhenMealsExist()
    {
        var food = new FoodEntry { Name = "Pizza", Quantity = 1 };
        var meal = new Meal
                   {
                       MealType   = MealType.Lunch
                     , Foods      = new List<FoodEntry> { food }
                     , ConsumedAt = new DateTime(2026, 8, 4, 13, 30, 0, DateTimeKind.Local)
                   };

        _mealServiceMock.Setup(service => service.ListAsync(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>()))
                        .ReturnsAsync(new List<Meal> { meal });

        var result = await _actions.ListMeals("today");

        Assert.True(result.Success);
        Assert.Contains("# Food Log", result.Message);
        Assert.Contains("Lunch",      result.Message);
        Assert.Contains("Pizza",      result.Message);
    }

    [Fact]
    public async Task DeleteMeal_SoftDeletes_ByMealType()
    {
        var id   = Guid.NewGuid();
        var meal = new Meal { Id = id.ToString("N"), MealType = MealType.Breakfast };

        _mealServiceMock.Setup(service => service.ListAsync(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>()))
                        .ReturnsAsync(new List<Meal> { meal });
        _mealServiceMock.Setup(service => service.SoftDeleteAsync(id))
                        .ReturnsAsync(true);

        var result = await _actions.DeleteMeal("breakfast");

        Assert.True(result.Success);
        Assert.Contains("Deleted Breakfast entry", result.Message);
        _mealServiceMock.Verify(service => service.SoftDeleteAsync(id), Times.Once);
    }

    [Fact]
    public async Task UpdateMeal_AppendsFoods_AndReturnsUpdatedMessage()
    {
        var id    = Guid.NewGuid();
        var meal  = new Meal { Id = id.ToString("N"), MealType = MealType.Breakfast };
        var food  = new FoodEntry { Name = "Apple", Quantity = 1 };

        _mealServiceMock.Setup(service => service.ListAsync(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>()))
                        .ReturnsAsync(new List<Meal> { meal });
        _mealServiceMock.Setup(service => service.UpdateAsync(id, It.IsAny<List<FoodEntry>>()))
                        .ReturnsAsync(new Meal { Id = id.ToString("N"), MealType = MealType.Breakfast, Foods = new List<FoodEntry> { food } });

        var result = await _actions.UpdateMeal("breakfast", new List<FoodEntry> { food });

        Assert.True(result.Success);
        Assert.Contains("Updated Breakfast by adding", result.Message);
        Assert.Contains("Apple",                      result.Message);
    }
}
