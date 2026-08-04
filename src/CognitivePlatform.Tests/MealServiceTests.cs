using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Domains.Meals;
using CognitivePlatform.Api.Workspace;
using Moq;
using Xunit;

namespace CognitivePlatform.Tests;

public class MealServiceTests
{
    private readonly Mock<IObjectStore>      _storeMock            = new();
    private readonly Mock<IWorkspaceContext> _workspaceContextMock = new();
    private readonly MealService             _service;

    public MealServiceTests()
    {
        _workspaceContextMock.Setup(context => context.ActivePartitionKey)
                             .Returns("test-workspace");

        _service = new MealService(_storeMock.Object, _workspaceContextMock.Object);
    }

    [Fact]
    public async Task SaveAsync_SavesMeal_WithPartitionKey()
    {
        var meal = new Meal { MealType = MealType.Breakfast };

        var result = await _service.SaveAsync(meal);

        Assert.Equal(meal, result);
        _storeMock.Verify(store => store.Save(meal, "test-workspace", meal.Id.ToString("N")), Times.Once);
    }

    [Fact]
    public async Task GetAsync_RetrievesMeal_WithPartitionKey()
    {
        var id   = Guid.NewGuid();
        var meal = new Meal { Id = id, MealType = MealType.Lunch };
        _storeMock.Setup(store => store.GetAsync<Meal>(id.ToString("N"), "test-workspace", default))
                  .ReturnsAsync(meal);

        var result = await _service.GetAsync(id);

        Assert.Equal(meal, result);
    }

    [Fact]
    public async Task ListAsync_FiltersOut_SoftDeletedMeals()
    {
        var activeMeal  = new Meal { Id = Guid.NewGuid(), MealType = MealType.Dinner, IsDeleted = false };
        var deletedMeal = new Meal { Id = Guid.NewGuid(), MealType = MealType.Breakfast, IsDeleted = true };
        _storeMock.Setup(store => store.ListAsync<Meal>("test-workspace", null, null, default))
                  .ReturnsAsync(new List<Meal> { activeMeal, deletedMeal });

        var result = await _service.ListAsync();

        Assert.Single(result);
        Assert.Equal(activeMeal.Id, result[0].Id);
    }

    [Fact]
    public async Task SoftDeleteAsync_SetsDeletedFlags_AndSaves()
    {
        var id   = Guid.NewGuid();
        var meal = new Meal { Id = id, MealType = MealType.Lunch, IsDeleted = false };
        _storeMock.Setup(store => store.GetAsync<Meal>(id.ToString("N"), "test-workspace", default))
                  .ReturnsAsync(meal);

        var result = await _service.SoftDeleteAsync(id);

        Assert.True(result);
        _storeMock.Verify(store => store.Save(It.Is<Meal>(m => m.Id == id && m.IsDeleted && m.DeletedUtc != null)
                                            , "test-workspace"
                                            , id.ToString("N")), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_AppendsFoods_AndSaves()
    {
        var id       = Guid.NewGuid();
        var food1    = new FoodEntry { Name = "Egg" };
        var food2    = new FoodEntry { Name = "Toast" };
        var meal     = new Meal { Id = id, Foods = new List<FoodEntry> { food1 } };
        
        _storeMock.Setup(store => store.GetAsync<Meal>(id.ToString("N"), "test-workspace", default))
                  .ReturnsAsync(meal);

        var result = await _service.UpdateAsync(id, new List<FoodEntry> { food2 });

        Assert.NotNull(result);
        Assert.Equal(2, result.Foods.Count);
        Assert.Equal("Egg",   result.Foods[0].Name);
        Assert.Equal("Toast", result.Foods[1].Name);
        
        _storeMock.Verify(store => store.Save(It.Is<Meal>(m => m.Id == id && m.Foods.Count == 2)
                                            , "test-workspace"
                                            , id.ToString("N")), Times.Once);
    }
}
