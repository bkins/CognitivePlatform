using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Domains.Meals;
using CognitivePlatform.Api.KnowledgeInbox;
using Moq;
using Xunit;

namespace CognitivePlatform.Tests;

public class MealKnowledgeSourceTests
{
    private readonly Mock<IMealService> _mealServiceMock = new();
    private readonly Mock<IObjectStore> _objectStoreMock = new();
    private readonly MealKnowledgeSource _source;

    public MealKnowledgeSourceTests()
    {
        _source = new MealKnowledgeSource(_mealServiceMock.Object, _objectStoreMock.Object);
    }

    [Fact]
    public void Kind_ReturnsMeal()
    {
        Assert.Equal(KnowledgeKind.Meal, _source.Kind);
    }

    [Fact]
    public void GetKnowledgeItems_ReturnsMappedItems_FromObjectStore()
    {
        var mealId = Guid.NewGuid().ToString("N");
        var meal = new Meal
                   {
                       Id         = mealId
                     , MealType   = MealType.Breakfast
                     , ConsumedAt = new DateTimeOffset(2026, 8, 14, 8, 30, 0, TimeSpan.Zero)
                     , Foods      = new List<FoodEntry>
                                    {
                                        new() { Name = "Oatmeal" }
                                      , new() { Name = "Coffee" }
                                    }
                   };

        _objectStoreMock.Setup(store => store.List<Meal>(null, null, null))
                        .Returns(new List<Meal> { meal });

        var items = _source.GetKnowledgeItems(new KnowledgeQuery(), CancellationToken.None).ToList();

        Assert.Single(items);
        var item = items[0];
        Assert.Equal(Guid.Parse(mealId), item.Id);
        Assert.Equal(KnowledgeKind.Meal, item.Kind);
        Assert.Contains("Breakfast", item.Title);
        Assert.Contains("2 item(s)", item.Title);
        Assert.Equal("Oatmeal, Coffee", item.Summary);
        Assert.Equal(KnowledgeStatus.Active, item.Status);
        Assert.Equal(new[] { "Oatmeal", "Coffee" }, item.Tags);
    }

    [Fact]
    public void GetKnowledgeItems_MarksStatusDeleted_WhenMealIsDeleted()
    {
        var mealId = Guid.NewGuid().ToString("N");
        var meal = new Meal
                   {
                       Id        = mealId
                     , IsDeleted = true
                     , Foods     = new List<FoodEntry>()
                   };

        _objectStoreMock.Setup(store => store.List<Meal>(null, null, null))
                        .Returns(new List<Meal> { meal });

        var items = _source.GetKnowledgeItems(new KnowledgeQuery(), CancellationToken.None).ToList();

        Assert.Single(items);
        Assert.Equal(KnowledgeStatus.Deleted, items[0].Status);
    }

    [Fact]
    public void ListHeaders_ReturnsObjectHeaders_ForNonDeletedMeals()
    {
        var meal1 = new Meal
                    {
                        Id         = Guid.NewGuid().ToString("N")
                      , ConsumedAt = DateTimeOffset.UtcNow
                    };
        var meal2 = new Meal
                    {
                        Id         = Guid.NewGuid().ToString("N")
                      , ConsumedAt = DateTimeOffset.UtcNow
                      , IsDeleted  = true
                    };

        _objectStoreMock.Setup(store => store.List<Meal>(null, It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>()))
                        .Returns(new List<Meal> { meal1, meal2 });

        var headers = _source.ListHeaders(null, null);

        Assert.Single(headers);
        Assert.Equal(meal1.Id, headers[0].Id);
        Assert.Equal("Meal", headers[0].Type);
    }

    [Fact]
    public void Archive_CallsObjectStoreSoftDelete()
    {
        var id = Guid.NewGuid();

        _source.Archive(id, CancellationToken.None);

        _objectStoreMock.Verify(store => store.SoftDelete<Meal>(id.ToString("N")), Times.Once);
    }
}
