using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CognitivePlatform.Api.Conversation;
using CognitivePlatform.Api.Domains.Journal;
using CognitivePlatform.Api.Domains.Journal.Interfaces;
using CognitivePlatform.Api.Domains.Meals;
using CognitivePlatform.Api.Domains.Tasks;
using CognitivePlatform.Api.Insights;
using CognitivePlatform.Api.Insights.Models;
using CognitivePlatform.Api.Integrations.Health;
using CognitivePlatform.Api.Integrations.Health.Models;
using CognitivePlatform.Api.Models;
using Moq;
using Xunit;

namespace CognitivePlatform.Tests;

public class MealInsightProviderTests
{
    private readonly Mock<IMealService>    _mealServiceMock    = new();
    private readonly Mock<IHealthProvider> _healthProviderMock = new();
    private readonly Mock<IJournalService> _journalServiceMock = new();
    private readonly Mock<ITaskService>    _taskServiceMock    = new();
    private readonly MealInsightProvider   _provider;

    public MealInsightProviderTests()
    {
        _provider = new MealInsightProvider( _mealServiceMock.Object
                                           , _healthProviderMock.Object
                                           , _journalServiceMock.Object
                                           , _taskServiceMock.Object );

        _journalServiceMock.Setup(service => service.ListEntries(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>()))
                           .Returns(new List<JournalEntryWithRevision>());
        _taskServiceMock.Setup(service => service.List(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), true))
                        .Returns(new List<TaskItem>());
    }

    [Fact]
    public async Task GenerateAsync_YieldsLateDinnerInsight_WhenLateDinnersCorrelateWithReducedSleep()
    {
        _healthProviderMock.Setup(provider => provider.IsConnected).Returns(true);

        var today = DateTimeOffset.Now.Date;
        var meals = new List<Meal>();

        for (var offset = 1; offset <= 5; offset++)
        {
            var day = today.AddDays(-offset);
            var lateDinner = new Meal
                             {
                                 MealType   = MealType.Dinner
                               , ConsumedAt = new DateTimeOffset(day.AddHours(21))
                             };
            meals.Add(lateDinner);

            var nightStart = new DateTimeOffset(day, TimeSpan.Zero);
            _healthProviderMock.Setup(provider => provider.GetSleepAsync(nightStart, nightStart.AddDays(1), It.IsAny<CancellationToken>()))
                               .ReturnsAsync(new SleepResult { TotalMinutes = 300 });
        }

        for (var offset = 6; offset <= 10; offset++)
        {
            var day = today.AddDays(-offset);
            var nightStart = new DateTimeOffset(day, TimeSpan.Zero);
            _healthProviderMock.Setup(provider => provider.GetSleepAsync(nightStart, nightStart.AddDays(1), It.IsAny<CancellationToken>()))
                               .ReturnsAsync(new SleepResult { TotalMinutes = 480 });
        }

        _mealServiceMock.Setup(service => service.ListAsync(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>()))
                        .ReturnsAsync(meals);

        var insights = await CollectInsightsAsync(_provider);

        Assert.NotEmpty(insights);
        Assert.Contains(insights, insight => insight.DeduplicationKey == "health.meals.latedinner");
    }

    [Fact]
    public async Task GenerateAsync_YieldsCaffeineInsight_WhenLateCaffeineCorrelatesWithReducedSleep()
    {
        _healthProviderMock.Setup(provider => provider.IsConnected).Returns(true);

        var today = DateTimeOffset.Now.Date;
        var meals = new List<Meal>();

        for (var offset = 1; offset <= 5; offset++)
        {
            var day = today.AddDays(-offset);
            var coffeeMeal = new Meal
                             {
                                 MealType   = MealType.Snack
                               , ConsumedAt = new DateTimeOffset(day.AddHours(16))
                               , Foods      = new List<FoodEntry> { new() { Name = "Coffee" } }
                             };
            meals.Add(coffeeMeal);

            var nightStart = new DateTimeOffset(day, TimeSpan.Zero);
            _healthProviderMock.Setup(provider => provider.GetSleepAsync(nightStart, nightStart.AddDays(1), It.IsAny<CancellationToken>()))
                               .ReturnsAsync(new SleepResult { TotalMinutes = 320 });
        }

        for (var offset = 6; offset <= 10; offset++)
        {
            var day = today.AddDays(-offset);
            var normalMeal = new Meal
                             {
                                 MealType   = MealType.Snack
                               , ConsumedAt = new DateTimeOffset(day.AddHours(10))
                               , Foods      = new List<FoodEntry> { new() { Name = "Apple" } }
                             };
            meals.Add(normalMeal);

            var nightStart = new DateTimeOffset(day, TimeSpan.Zero);
            _healthProviderMock.Setup(provider => provider.GetSleepAsync(nightStart, nightStart.AddDays(1), It.IsAny<CancellationToken>()))
                               .ReturnsAsync(new SleepResult { TotalMinutes = 480 });
        }

        _mealServiceMock.Setup(service => service.ListAsync(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>()))
                        .ReturnsAsync(meals);

        var insights = await CollectInsightsAsync(_provider);

        Assert.NotEmpty(insights);
        Assert.Contains(insights, insight => insight.DeduplicationKey == "health.meals.caffeine");
    }

    [Fact]
    public async Task GenerateAsync_YieldsMoodFoodInsight_WhenLowMoodCorrelatesWithSugarOrAlcohol()
    {
        _healthProviderMock.Setup(provider => provider.IsConnected).Returns(false);

        var today = DateTimeOffset.Now.Date;
        var meals = new List<Meal>();
        var journals = new List<JournalEntryWithRevision>();

        for (var offset = 1; offset <= 5; offset++)
        {
            var day = today.AddDays(-offset);
            var sugarMeal = new Meal
                            {
                                MealType   = MealType.Snack
                              , ConsumedAt = new DateTimeOffset(day.AddHours(15))
                              , Foods      = new List<FoodEntry> { new() { Name = "Soda", Additions = new List<string> { "high sugar" } } }
                            };
            meals.Add(sugarMeal);
            journals.Add(MakeMoodEntry(new DateTimeOffset(day.AddHours(20)), 1));
        }

        _mealServiceMock.Setup(service => service.ListAsync(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>()))
                        .ReturnsAsync(meals);
        _journalServiceMock.Setup(service => service.ListEntries(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>()))
                           .Returns(journals);

        var insights = await CollectInsightsAsync(_provider);

        Assert.NotEmpty(insights);
        Assert.Contains(insights, insight => insight.DeduplicationKey == "meal.insight.mood_food_correlation");
    }

    [Fact]
    public async Task GenerateAsync_YieldsProductivityProteinInsight_WhenHighProteinBreakfastCorrelatesWithTaskCompletion()
    {
        _healthProviderMock.Setup(provider => provider.IsConnected).Returns(false);

        var today = DateTimeOffset.Now.Date;
        var meals = new List<Meal>();
        var tasks = new List<TaskItem>();

        for (var offset = 1; offset <= 5; offset++)
        {
            var day = today.AddDays(-offset);
            var breakfast = new Meal
                            {
                                MealType   = MealType.Breakfast
                              , ConsumedAt = new DateTimeOffset(day.AddHours(8))
                              , Foods      = new List<FoodEntry>
                                             {
                                                 new()
                                                 {
                                                     Name      = "Eggs and Greek Yogurt"
                                                   , Nutrition = new NutritionalInfo { ProteinGrams = 28.0 }
                                                 }
                                             }
                            };
            meals.Add(breakfast);

            for (var taskIndex = 0; taskIndex < 4; taskIndex++)
            {
                tasks.Add(new TaskItem
                          {
                              Id               = Guid.NewGuid().ToString("N")
                            , ShortDescription = $"Task {taskIndex}"
                            , CompletedAt      = new DateTimeOffset(day.AddHours(14))
                          });
            }
        }

        for (var offset = 6; offset <= 10; offset++)
        {
            var day = today.AddDays(-offset);
            var lowProteinBreakfast = new Meal
                                      {
                                          MealType   = MealType.Breakfast
                                        , ConsumedAt = new DateTimeOffset(day.AddHours(8))
                                        , Foods      = new List<FoodEntry>
                                                       {
                                                           new()
                                                           {
                                                               Name      = "Toast"
                                                             , Nutrition = new NutritionalInfo { ProteinGrams = 5.0 }
                                                           }
                                                       }
                                      };
            meals.Add(lowProteinBreakfast);
            tasks.Add(new TaskItem
                      {
                          Id               = Guid.NewGuid().ToString("N")
                        , ShortDescription = "Single task"
                        , CompletedAt      = new DateTimeOffset(day.AddHours(15))
                      });
        }

        _mealServiceMock.Setup(service => service.ListAsync(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>()))
                        .ReturnsAsync(meals);
        _taskServiceMock.Setup(service => service.List(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), true))
                        .Returns(tasks);

        var insights = await CollectInsightsAsync(_provider);

        Assert.NotEmpty(insights);
        Assert.Contains(insights, insight => insight.DeduplicationKey == "meal.insight.productivity_protein");
    }

    [Fact]
    public async Task GenerateAsync_YieldsNothing_WhenDataPointsAreBelowThreshold()
    {
        _healthProviderMock.Setup(provider => provider.IsConnected).Returns(true);

        var meals = new List<Meal>
                    {
                        new() { MealType = MealType.Dinner, ConsumedAt = DateTimeOffset.Now.AddDays(-1).AddHours(22) }
                    };

        _mealServiceMock.Setup(service => service.ListAsync(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>()))
                        .ReturnsAsync(meals);

        var insights = await CollectInsightsAsync(_provider);

        Assert.Empty(insights);
    }

    private static JournalEntryWithRevision MakeMoodEntry(DateTimeOffset createdUtc, int moodScore)
        => new(
            new JournalEntry { Id = Guid.NewGuid().ToString("N"), CreatedUtc = createdUtc }
          , new JournalRevision
            {
                RevisionId = Guid.NewGuid().ToString("N")
              , EntryId    = Guid.NewGuid().ToString("N")
              , Text       = "Journal entry with mood."
              , MoodScore  = moodScore
            }
          , IsEdited: false);

    private static async Task<List<Insight>> CollectInsightsAsync(MealInsightProvider provider)
    {
        var list    = new List<Insight>();
        var context = new ConversationContext("test-session");

        await foreach (var insight in provider.GenerateAsync(context))
        {
            list.Add(insight);
        }

        return list;
    }
}
