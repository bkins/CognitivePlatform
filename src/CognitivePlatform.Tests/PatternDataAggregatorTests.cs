using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CognitivePlatform.Api.Domains.Activity;
using CognitivePlatform.Api.Domains.Insights;
using CognitivePlatform.Api.Domains.Journal;
using CognitivePlatform.Api.Domains.Journal.Interfaces;
using CognitivePlatform.Api.Domains.Meals;
using CognitivePlatform.Api.Domains.Tasks;
using CognitivePlatform.Api.Models;
using Moq;
using Xunit;

namespace CognitivePlatform.Tests;

public class PatternDataAggregatorTests
{
    private readonly Mock<ITaskService>    _tasksMock    = new();
    private readonly Mock<IJournalService> _journalMock  = new();
    private readonly Mock<IActivityLog>    _activityMock = new();
    private readonly Mock<IMealService>    _mealsMock    = new();
    private readonly PatternDataAggregator _aggregator;

    public PatternDataAggregatorTests()
    {
        _tasksMock.Setup(service => service.List(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), true))
                  .Returns(new List<TaskItem>());
        _journalMock.Setup(service => service.ListEntries(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>()))
                    .Returns(new List<JournalEntryWithRevision>());
        _activityMock.Setup(log => log.ListAsync(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
                     .ReturnsAsync((IReadOnlyList<ActivityEvent>)Array.Empty<ActivityEvent>());
        _mealsMock.Setup(service => service.ListAsync(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>()))
                  .ReturnsAsync(new List<Meal>());

        _aggregator = new PatternDataAggregator( _tasksMock.Object
                                               , _journalMock.Object
                                               , _activityMock.Object
                                               , _mealsMock.Object );
    }

    [Fact]
    public async Task AggregateAndFormatAsync_ReturnsNull_WhenNoDataExists()
    {
        var result = await _aggregator.AggregateAndFormatAsync();

        Assert.Null(result);
    }

    [Fact]
    public async Task AggregateAndFormatAsync_IncludesTasksAndJournal_WhenDataExists()
    {
        var tasks = new List<TaskItem>
                    {
                        new() { ShortDescription = "Write specification" }
                    };
        var journals = new List<JournalEntryWithRevision>
                       {
                           new(
                               new JournalEntry { Id = Guid.NewGuid().ToString("N"), CreatedUtc = DateTimeOffset.UtcNow }
                             , new JournalRevision
                               {
                                   RevisionId = Guid.NewGuid().ToString("N")
                                 , EntryId    = Guid.NewGuid().ToString("N")
                                 , Text       = "Productive morning session."
                               }
                             , IsEdited: false)
                       };

        _tasksMock.Setup(service => service.List(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), true))
                  .Returns(tasks);
        _journalMock.Setup(service => service.ListEntries(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>()))
                    .Returns(journals);

        var result = await _aggregator.AggregateAndFormatAsync();

        Assert.NotNull(result);
        Assert.Contains("=== Tasks ===", result!);
        Assert.Contains("Write specification", result);
        Assert.Contains("=== Journal Entries ===", result);
        Assert.Contains("Productive morning session.", result);
    }

    [Fact]
    public async Task AggregateAndFormatAsync_IncludesMealsSection_WhenMealsExist()
    {
        var meals = new List<Meal>
                    {
                        new()
                        {
                            MealType   = MealType.Breakfast
                          , ConsumedAt = DateTimeOffset.UtcNow
                          , Foods      = new List<FoodEntry> { new() { Name = "Oatmeal" }, new() { Name = "Blueberries" } }
                          , Notes      = "Pre-workout meal"
                        }
                    };

        _mealsMock.Setup(service => service.ListAsync(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>()))
                  .ReturnsAsync(meals);

        var result = await _aggregator.AggregateAndFormatAsync();

        Assert.NotNull(result);
        Assert.Contains("=== Meals ===", result!);
        Assert.Contains("Breakfast", result);
        Assert.Contains("Oatmeal, Blueberries", result);
        Assert.Contains("Pre-workout meal", result);
    }

    [Fact]
    public async Task AggregateAndFormatAsync_IncludesFocusLabel_WhenProvided()
    {
        var tasks = new List<TaskItem>
                    {
                        new() { ShortDescription = "Task sample" }
                    };
        _tasksMock.Setup(service => service.List(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), true))
                  .Returns(tasks);

        var result = await _aggregator.AggregateAndFormatAsync(focus: "sleep and exercise");

        Assert.NotNull(result);
        Assert.Contains("Focus area: sleep and exercise", result!);
    }

    [Fact]
    public async Task AggregateAndFormatAsync_IncludesTaskStatusLabels_InResult()
    {
        var completedTask = new TaskItem
                            {
                                ShortDescription = "Done task"
                              , CompletedAt      = DateTimeOffset.UtcNow
                            };
        var deletedTask = new TaskItem
                          {
                              ShortDescription = "Deleted task"
                            , IsDeleted        = true
                          };
        var activeTask  = new TaskItem { ShortDescription = "Active task" };

        _tasksMock.Setup(service => service.List(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), true))
                  .Returns(new List<TaskItem> { completedTask, deletedTask, activeTask });
        _journalMock.Setup(service => service.ListEntries(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>()))
                    .Returns(new List<JournalEntryWithRevision>());

        var result = await _aggregator.AggregateAndFormatAsync();

        Assert.NotNull(result);
        Assert.Contains("[Done]",    result!);
        Assert.Contains("[Deleted]", result);
        Assert.Contains("[Active]",  result);
    }

    [Fact]
    public async Task AggregateAndFormatAsync_IncludesActivitySection_WhenEventsExist()
    {
        _tasksMock.Setup(service => service.List(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), true))
                  .Returns(new List<TaskItem> { new() { ShortDescription = "Write code" } });

        _activityMock.Setup(log => log.ListAsync(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
                     .ReturnsAsync((IReadOnlyList<ActivityEvent>)new List<ActivityEvent>
                                                                 {
                                                                     new()
                                                                     {
                                                                         ActivityType = "run"
                                                                       , Duration     = 30
                                                                       , Unit         = "minutes"
                                                                       , OccurredUtc  = DateTimeOffset.UtcNow
                                                                     }
                                                                 });

        var result = await _aggregator.AggregateAndFormatAsync();

        Assert.NotNull(result);
        Assert.Contains("=== Recent Activities ===", result!);
        Assert.Contains("run",                        result);
        Assert.Contains("30 minutes",                 result);
    }

    [Fact]
    public async Task AggregateAndFormatAsync_OmitsActivitySection_WhenNoEvents()
    {
        _tasksMock.Setup(service => service.List(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), true))
                  .Returns(new List<TaskItem> { new() { ShortDescription = "Write code" } });

        var result = await _aggregator.AggregateAndFormatAsync();

        Assert.NotNull(result);
        Assert.DoesNotContain("Recent Activities", result!);
    }

    [Fact]
    public async Task AggregateAndFormatAsync_OmitsActivitySection_WhenActivityLogIsNull()
    {
        _tasksMock.Setup(service => service.List(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), true))
                  .Returns(new List<TaskItem> { new() { ShortDescription = "Only task" } });

        var aggregatorNoActivity = new PatternDataAggregator(_tasksMock.Object, _journalMock.Object);

        var result = await aggregatorNoActivity.AggregateAndFormatAsync();

        Assert.NotNull(result);
        Assert.DoesNotContain("Recent Activities", result!);
    }
}

