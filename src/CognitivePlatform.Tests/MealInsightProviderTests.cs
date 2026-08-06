using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CognitivePlatform.Api.Conversation;
using CognitivePlatform.Api.Domains.Journal.Interfaces;
using CognitivePlatform.Api.Domains.Meals;
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
    private readonly MealInsightProvider   _provider;

    public MealInsightProviderTests()
    {
        _provider = new MealInsightProvider( _mealServiceMock.Object
                                           , _healthProviderMock.Object
                                           , _journalServiceMock.Object );

        _journalServiceMock.Setup(service => service.ListEntries(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>()))
                           .Returns(new List<JournalEntryWithRevision>());
    }

    [Fact]
    public async Task GenerateAsync_YieldsLateDinnerInsight_WhenLateDinnersCorrelateWithReducedSleep()
    {
        _healthProviderMock.Setup(provider => provider.IsConnected).Returns(true);

        var today = DateTimeOffset.Now.Date;
        var meals = new List<Meal>();

        // 5 consecutive days of late dinners (9 PM) corresponding to poor sleep (300 minutes / 5 hours)
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

        // 5 days of normal sleep (480 mins / 8 hours) with early or no late dinner
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
