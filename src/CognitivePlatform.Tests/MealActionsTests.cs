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

    [Fact]
    public async Task LogMeal_WithIdenticalExistingMealOnSameDay_ReturnsExistingWithoutDuplicating()
    {
        var food = new FoodEntry { Name = "Pizza", Quantity = 1 };
        var existingMeal = new Meal
                           {
                               Id         = Guid.NewGuid().ToString("N")
                             , MealType   = MealType.Lunch
                             , Foods      = new List<FoodEntry> { food }
                             , ConsumedAt = new DateTimeOffset(2026, 8, 4, 13, 30, 0, TimeSpan.Zero)
                           };
        var incomingMeal = new Meal
                           {
                               MealType   = MealType.Lunch
                             , Foods      = new List<FoodEntry> { new FoodEntry { Name = "Pizza", Quantity = 1 } }
                             , ConsumedAt = new DateTimeOffset(2026, 8, 4, 13, 32, 0, TimeSpan.Zero)
                           };

        _mealServiceMock.Setup(service => service.ListAsync(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>()))
                        .ReturnsAsync(new List<Meal> { existingMeal });

        var result = await _actions.LogMeal(incomingMeal);

        Assert.True(result.Success);
        Assert.Contains("already logged", result.Message, StringComparison.OrdinalIgnoreCase);
        _mealServiceMock.Verify(service => service.SaveAsync(It.IsAny<Meal>()), Times.Never);
    }

    [Fact]
    public async Task LogMeal_WithNewItemsForExistingMealTypeOnSameDay_MergesItemsIntoExistingEntry()
    {
        var existingId = Guid.NewGuid();
        var existingMeal = new Meal
                           {
                               Id         = existingId.ToString("N")
                             , MealType   = MealType.Lunch
                             , Foods      = new List<FoodEntry> { new FoodEntry { Name = "Pizza", Quantity = 1 } }
                             , ConsumedAt = new DateTimeOffset(2026, 8, 4, 13, 30, 0, TimeSpan.Zero)
                           };
        var incomingMeal = new Meal
                           {
                               MealType   = MealType.Lunch
                             , Foods      = new List<FoodEntry>
                                            {
                                                new FoodEntry { Name = "Pizza", Quantity = 1 }
                                              , new FoodEntry { Name = "Salad", Quantity = 1 }
                                            }
                             , ConsumedAt = new DateTimeOffset(2026, 8, 4, 13, 35, 0, TimeSpan.Zero)
                           };
        var updatedMeal = new Meal
                          {
                              Id         = existingId.ToString("N")
                            , MealType   = MealType.Lunch
                            , Foods      = new List<FoodEntry>
                                           {
                                               new FoodEntry { Name = "Pizza", Quantity = 1 }
                                             , new FoodEntry { Name = "Salad", Quantity = 1 }
                                           }
                            , ConsumedAt = new DateTimeOffset(2026, 8, 4, 13, 30, 0, TimeSpan.Zero)
                          };

        _mealServiceMock.Setup(service => service.ListAsync(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>()))
                        .ReturnsAsync(new List<Meal> { existingMeal });
        _mealServiceMock.Setup(service => service.UpdateAsync(existingId, It.IsAny<List<FoodEntry>>()))
                        .ReturnsAsync(updatedMeal);

        var result = await _actions.LogMeal(incomingMeal);

        Assert.True(result.Success);
        Assert.Contains("Added 1 new item(s) to today's Lunch entry", result.Message, StringComparison.OrdinalIgnoreCase);
        _mealServiceMock.Verify(service => service.UpdateAsync(existingId, It.Is<List<FoodEntry>>(foods => foods.Count == 1 && foods[0].Name == "Salad")), Times.Once);
        _mealServiceMock.Verify(service => service.SaveAsync(It.IsAny<Meal>()), Times.Never);
    }

    [Fact]
    public async Task LogMeal_WithSnackHavingIdenticalItemsOnSameDay_DoesNotDuplicate()
    {
        var existingMeal = new Meal
                           {
                               Id         = Guid.NewGuid().ToString("N")
                             , MealType   = MealType.Snack
                             , Foods      = new List<FoodEntry> { new FoodEntry { Name = "Almonds", Quantity = 1 } }
                             , ConsumedAt = new DateTimeOffset(2026, 8, 4, 15, 0, 0, TimeSpan.Zero)
                           };
        var incomingMeal = new Meal
                           {
                               MealType   = MealType.Snack
                             , Foods      = new List<FoodEntry> { new FoodEntry { Name = "Almonds", Quantity = 1 } }
                             , ConsumedAt = new DateTimeOffset(2026, 8, 4, 15, 5, 0, TimeSpan.Zero)
                           };

        _mealServiceMock.Setup(service => service.ListAsync(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>()))
                        .ReturnsAsync(new List<Meal> { existingMeal });

        var result = await _actions.LogMeal(incomingMeal);

        Assert.True(result.Success);
        Assert.Contains("already logged", result.Message, StringComparison.OrdinalIgnoreCase);
        _mealServiceMock.Verify(service => service.SaveAsync(It.IsAny<Meal>()), Times.Never);
    }
    [Fact]
    public async Task LogMeals_SavesBothMeals_AndReturnsConsolidatedMessage()
    {
        var lunch = new Meal
                    {
                        MealType   = MealType.Lunch
                      , Foods      = new List<FoodEntry> { new FoodEntry { Name = "turkey sandwich" } }
                      , ConsumedAt = new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.Zero)
                    };
        var dinner = new Meal
                     {
                         MealType   = MealType.Dinner
                       , Foods      = new List<FoodEntry>
                                      {
                                          new FoodEntry { Name = "grilled salmon" }
                                        , new FoodEntry { Name = "rice" }
                                      }
                       , ConsumedAt = new DateTimeOffset(2026, 8, 9, 18, 0, 0, TimeSpan.Zero)
                     };

        _mealServiceMock.Setup(service => service.ListAsync(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>()))
                        .ReturnsAsync(new List<Meal>());
        _mealServiceMock.Setup(service => service.SaveAsync(lunch)).ReturnsAsync(lunch);
        _mealServiceMock.Setup(service => service.SaveAsync(dinner)).ReturnsAsync(dinner);

        var result = await _actions.LogMeals(new List<Meal> { lunch, dinner });

        Assert.True(result.Success);
        Assert.Contains("Logged Lunch",   result.Message);
        Assert.Contains("Logged Dinner",  result.Message);
        Assert.Contains("turkey sandwich", result.Message);
        Assert.Contains("grilled salmon",  result.Message);
        _mealServiceMock.Verify(service => service.SaveAsync(It.IsAny<Meal>()), Times.Exactly(2));
    }

    [Fact]
    public async Task LogMeals_WithNullInput_ReturnsFailed()
    {
        var result = await _actions.LogMeals(null!);

        Assert.False(result.Success);
        Assert.Contains("No meal details", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LogMeals_WithEmptyList_ReturnsFailed()
    {
        var result = await _actions.LogMeals(new List<Meal>());

        Assert.False(result.Success);
    }

    [Fact]
    public async Task LogMeal_SingleItemWithAdditions_IncludesAdditionsInMessage()
    {
        var food = new FoodEntry
                   {
                       Name      = "oatmeal with blueberries"
                     , Additions = new List<string> { "black coffee" }
                   };
        var meal = new Meal
                   {
                       MealType   = MealType.Breakfast
                     , Foods      = new List<FoodEntry> { food }
                     , ConsumedAt = new DateTimeOffset(2026, 8, 9, 8, 0, 0, TimeSpan.Zero)
                   };

        _mealServiceMock.Setup(service => service.ListAsync(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>()))
                        .ReturnsAsync(new List<Meal>());
        _mealServiceMock.Setup(service => service.SaveAsync(meal)).ReturnsAsync(meal);

        var result = await _actions.LogMeal(meal);

        Assert.True(result.Success);
        Assert.Contains("oatmeal with blueberries",   result.Message);
        Assert.Contains("black coffee",               result.Message);
    }
}
