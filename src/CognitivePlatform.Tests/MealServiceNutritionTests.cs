using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Domains.Meals;
using CognitivePlatform.Api.Workspace;
using Moq;
using Xunit;

namespace CognitivePlatform.Tests;

public class MealServiceNutritionTests
{
    private readonly Mock<IObjectStore>            _storeMock            = new();
    private readonly Mock<IWorkspaceContext>       _workspaceContextMock = new();
    private readonly Mock<INutritionLookupService> _nutritionMock        = new();
    private readonly MealService                   _service;

    public MealServiceNutritionTests()
    {
        _workspaceContextMock.Setup(context => context.ActivePartitionKey)
                             .Returns("test-workspace");

        _service = new MealService( _storeMock.Object
                                  , _workspaceContextMock.Object
                                  , _nutritionMock.Object );
    }

    [Fact]
    public async Task SaveAsync_EnrichesFoodEntry_WhenNutritionIsNull()
    {
        var food = new FoodEntry
                   {
                       Name     = "Banana"
                     , Quantity = 1
                     , Unit     = "medium"
                   };
        var meal = new Meal
                   {
                       MealType = MealType.Breakfast
                     , Foods    = new List<FoodEntry> { food }
                   };
        var nutrition = new NutritionalInfo
                        {
                            Calories     = 105
                          , ProteinGrams = 1.3
                          , CarbsGrams   = 27.0
                          , FatGrams     = 0.3
                          , FiberGrams   = 3.1
                        };

        _nutritionMock.Setup(service => service.LookupAsync("Banana", 1.0, "medium", It.IsAny<CancellationToken>()))
                      .ReturnsAsync(nutrition);

        var result = await _service.SaveAsync(meal);

        Assert.NotNull(result.Foods[0].Nutrition);
        Assert.Equal(105, result.Foods[0].Nutrition?.Calories);
        _nutritionMock.Verify(service => service.LookupAsync("Banana", 1.0, "medium", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SaveAsync_DoesNotLookup_WhenNutritionIsAlreadyPresent()
    {
        var existingNutrition = new NutritionalInfo { Calories = 250 };
        var food = new FoodEntry
                   {
                       Name      = "Apple"
                     , Quantity  = 1
                     , Nutrition = existingNutrition
                   };
        var meal = new Meal
                   {
                       MealType = MealType.Snack
                     , Foods    = new List<FoodEntry> { food }
                   };

        var result = await _service.SaveAsync(meal);

        Assert.Equal(250, result.Foods[0].Nutrition?.Calories);
        _nutritionMock.Verify(service => service.LookupAsync(It.IsAny<string>(), It.IsAny<double?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
