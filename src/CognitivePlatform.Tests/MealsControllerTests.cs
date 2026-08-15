using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CognitivePlatform.Api.Controllers;
using CognitivePlatform.Api.Domains.Meals;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace CognitivePlatform.Tests;

public class MealsControllerTests
{
    private readonly Mock<IMealService> _mealServiceMock = new();
    private readonly MealsController   _controller;

    public MealsControllerTests()
    {
        _controller = new MealsController(_mealServiceMock.Object);
    }

    [Fact]
    public async Task GetToday_ReturnsOkWithMeals_WhenMealsExist()
    {
        var meal = new Meal
                   {
                       Id         = Guid.NewGuid().ToString("N")
                     , MealType   = MealType.Breakfast
                     , ConsumedAt = DateTimeOffset.UtcNow
                   };

        _mealServiceMock.Setup(service => service.ListAsync(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>()))
                        .ReturnsAsync(new List<Meal> { meal });

        var result = await _controller.GetToday();

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var meals    = Assert.IsAssignableFrom<IReadOnlyList<Meal>>(okResult.Value);
        Assert.Single(meals);
        Assert.Equal(meal.Id, meals[0].Id);
    }

    [Fact]
    public async Task GetById_ReturnsNotFound_WhenMealDoesNotExist()
    {
        var id = Guid.NewGuid();
        _mealServiceMock.Setup(service => service.GetAsync(id))
                        .ReturnsAsync((Meal?)null);

        var result = await _controller.GetById(id);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetById_ReturnsNotFound_WhenMealIsDeleted()
    {
        var id   = Guid.NewGuid();
        var meal = new Meal
                   {
                       Id        = id.ToString("N")
                     , IsDeleted = true
                   };

        _mealServiceMock.Setup(service => service.GetAsync(id))
                        .ReturnsAsync(meal);

        var result = await _controller.GetById(id);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetById_ReturnsOkWithMeal_WhenMealExistsAndIsNotDeleted()
    {
        var id   = Guid.NewGuid();
        var meal = new Meal
                   {
                       Id       = id.ToString("N")
                     , MealType = MealType.Lunch
                   };

        _mealServiceMock.Setup(service => service.GetAsync(id))
                        .ReturnsAsync(meal);

        var result = await _controller.GetById(id);

        var okResult   = Assert.IsType<OkObjectResult>(result.Result);
        var returnedMeal = Assert.IsType<Meal>(okResult.Value);
        Assert.Equal(id.ToString("N"), returnedMeal.Id);
    }

    [Fact]
    public async Task GetRange_ReturnsBadRequest_WhenOnlyOneBoundProvided()
    {
        var result = await _controller.GetRange("2026-08-01", null);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetRange_ReturnsBadRequest_WhenFromIsAfterTo()
    {
        var result = await _controller.GetRange("2026-08-10", "2026-08-01");

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetRange_ReturnsOkWithMeals_WhenRangeIsValid()
    {
        var meal = new Meal
                   {
                       Id       = Guid.NewGuid().ToString("N")
                     , MealType = MealType.Dinner
                   };

        _mealServiceMock.Setup(service => service.ListAsync(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>()))
                        .ReturnsAsync(new List<Meal> { meal });

        var result = await _controller.GetRange("2026-08-01", "2026-08-05");

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var meals    = Assert.IsAssignableFrom<IReadOnlyList<Meal>>(okResult.Value);
        Assert.Single(meals);
    }

    [Fact]
    public async Task GetSummary_ReturnsCalculatedSummary_WithAggregatedMacros()
    {
        var food1 = new FoodEntry
                    {
                        Name      = "Egg"
                      , Nutrition = new NutritionalInfo
                                    {
                                        Calories     = 140
                                      , ProteinGrams = 12
                                      , CarbsGrams   = 1
                                      , FatGrams     = 10
                                      , FiberGrams   = 0
                                    }
                    };
        var food2 = new FoodEntry
                    {
                        Name      = "Toast"
                      , Nutrition = new NutritionalInfo
                                    {
                                        Calories     = 160
                                      , ProteinGrams = 6
                                      , CarbsGrams   = 28
                                      , FatGrams     = 2
                                      , FiberGrams   = 3
                                    }
                    };
        var meal = new Meal
                   {
                       Id         = Guid.NewGuid().ToString("N")
                     , MealType   = MealType.Breakfast
                     , Foods      = new List<FoodEntry> { food1, food2 }
                     , ConsumedAt = DateTimeOffset.UtcNow
                   };

        _mealServiceMock.Setup(service => service.ListAsync(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>()))
                        .ReturnsAsync(new List<Meal> { meal });

        var result = await _controller.GetSummary("2026-08-01", "2026-08-02");

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var summary  = Assert.IsType<NutritionSummaryDto>(okResult.Value);

        Assert.Equal(1, summary.TotalMeals);
        Assert.Equal(2, summary.TotalFoodItems);
        Assert.Equal(2, summary.EnrichedFoodItemsCount);
        Assert.Equal(300, summary.TotalCalories);
        Assert.Equal(18, summary.TotalProteinGrams);
        Assert.Equal(29, summary.TotalCarbsGrams);
        Assert.Equal(12, summary.TotalFatGrams);
        Assert.Equal(3, summary.TotalFiberGrams);
    }

    [Fact]
    public async Task Create_ReturnsBadRequest_WhenPayloadIsNull()
    {
        var result = await _controller.Create(null!);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Create_ReturnsCreatedAtAction_WhenMealIsSaved()
    {
        var meal = new Meal
                   {
                       Id       = Guid.NewGuid().ToString("N")
                     , MealType = MealType.Breakfast
                   };

        _mealServiceMock.Setup(service => service.SaveAsync(meal))
                        .ReturnsAsync(meal);

        var result = await _controller.Create(meal);

        var createdResult = Assert.IsType<CreatedAtActionResult>(result.Result);
        var savedMeal     = Assert.IsType<Meal>(createdResult.Value);
        Assert.Equal(meal.Id, savedMeal.Id);
    }

    [Fact]
    public async Task Update_ReturnsNotFound_WhenMealDoesNotExist()
    {
        var id   = Guid.NewGuid();
        var food = new FoodEntry { Name = "Banana" };

        _mealServiceMock.Setup(service => service.UpdateAsync(id, It.IsAny<List<FoodEntry>>()))
                        .ReturnsAsync((Meal?)null);

        var result = await _controller.Update(id, new List<FoodEntry> { food });

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task Update_ReturnsOkWithUpdatedMeal_WhenUpdateSucceeds()
    {
        var id          = Guid.NewGuid();
        var food        = new FoodEntry { Name = "Banana" };
        var updatedMeal = new Meal
                          {
                              Id    = id.ToString("N")
                            , Foods = new List<FoodEntry> { food }
                          };

        _mealServiceMock.Setup(service => service.UpdateAsync(id, It.IsAny<List<FoodEntry>>()))
                        .ReturnsAsync(updatedMeal);

        var result = await _controller.Update(id, new List<FoodEntry> { food });

        var okResult    = Assert.IsType<OkObjectResult>(result.Result);
        var returnedMeal = Assert.IsType<Meal>(okResult.Value);
        Assert.Single(returnedMeal.Foods);
    }

    [Fact]
    public async Task Delete_ReturnsNotFound_WhenMealNotFoundOrAlreadyDeleted()
    {
        var id = Guid.NewGuid();
        _mealServiceMock.Setup(service => service.SoftDeleteAsync(id))
                        .ReturnsAsync(false);

        var result = await _controller.Delete(id);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task Delete_ReturnsOk_WhenSoftDeleteSucceeds()
    {
        var id = Guid.NewGuid();
        _mealServiceMock.Setup(service => service.SoftDeleteAsync(id))
                        .ReturnsAsync(true);

        var result = await _controller.Delete(id);

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Contains("deleted", okResult.Value?.ToString(), StringComparison.OrdinalIgnoreCase);
    }
}
