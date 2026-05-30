using CognitivePlatform.Api.Domains.DailyRecord;
using CognitivePlatform.Api.Domains.Journal.Interfaces;
using CognitivePlatform.Api.Domains.Tasks;
using CognitivePlatform.Api.Integrations.Health;
using CognitivePlatform.Api.Integrations.Health.Models;
using CognitivePlatform.Api.Models;
using CognitivePlatform.Api.Wellbeing;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace CognitivePlatform.Tests.Wellbeing;

public sealed class WellbeingSignalCollectorTests
{
    private readonly Mock<IHealthProvider>       _healthMock       = new();
    private readonly Mock<ITaskService>          _tasksMock        = new();
    private readonly Mock<IJournalService>       _journalMock      = new();
    private readonly Mock<IDailyRecordService>   _dailyRecordMock  = new();
    private readonly Mock<IWellbeingSignalStore> _storeMock        = new();

    private readonly WellbeingSignalCollector _collector;

    private static readonly DateOnly TestDate = new(2026, 5, 29);

    public WellbeingSignalCollectorTests()
    {
        _storeMock
            .Setup(store => store.SaveSignalAsync(It.IsAny<WellbeingSignal>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _collector = new WellbeingSignalCollector(
            _healthMock.Object
          , _tasksMock.Object
          , _journalMock.Object
          , _dailyRecordMock.Object
          , _storeMock.Object
          , NullLogger<WellbeingSignalCollector>.Instance);
    }

    // ====================================================================
    // Helpers
    // ====================================================================

    private static JournalEntryWithRevision MakeJournalEntry(string text) =>
        new(new Api.Domains.Journal.JournalEntry
            {
                    Id         = Guid.NewGuid().ToString()
                  , CreatedUtc = DateTimeOffset.UtcNow
            }
          , new Api.Domains.Journal.JournalRevision
            {
                    RevisionId = Guid.NewGuid().ToString()
                  , EntryId    = Guid.NewGuid().ToString()
                  , CreatedUtc = DateTimeOffset.UtcNow
                  , Text       = text
            }
          , IsEdited: false);

    private static TaskItem MakeTask(DateTimeOffset? dueDate = null, DateTimeOffset? completedAt = null) =>
        new()
        {
                Id          = Guid.NewGuid().ToString("N")
              , DueDate     = dueDate
              , CompletedAt = completedAt
        };

    private DateTimeOffset DayStart => new(TestDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
    private DateTimeOffset DayEnd   => DayStart.AddDays(1);

    // ====================================================================
    // Health signals
    // ====================================================================

    [Fact]
    public async Task CollectSignalsAsync_IncludesHealthSignals_WhenHealthIsConnected()
    {
        _healthMock.SetupGet(provider => provider.IsConnected).Returns(true);
        _healthMock
            .Setup(provider => provider.GetStepCountAsync(DayStart, DayEnd, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StepCountResult { Steps = 8500 });
        _healthMock
            .Setup(provider => provider.GetSleepAsync(DayStart, DayEnd, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SleepResult { TotalMinutes = 420 });
        _healthMock
            .Setup(provider => provider.GetHeartRateAsync(DayStart, DayEnd, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HeartRateResult { AverageBpm = 62 });
        _healthMock
            .Setup(provider => provider.GetDistanceAsync(DayStart, DayEnd, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DistanceResult { Metres = 6300.0 });

        _tasksMock.Setup(service => service.List(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<bool>())).Returns([]);
        _journalMock.Setup(service => service.ListEntries(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>())).Returns([]);
        _dailyRecordMock.Setup(service => service.GetByDate(TestDate)).Returns((DailyRecord?)null);

        var signals = await _collector.CollectSignalsAsync(TestDate);

        Assert.Contains(signals, signal => signal.MetricName == WellbeingMetrics.Steps          && signal.Value == 8500);
        Assert.Contains(signals, signal => signal.MetricName == WellbeingMetrics.SleepMinutes   && signal.Value == 420);
        Assert.Contains(signals, signal => signal.MetricName == WellbeingMetrics.HeartRateAvg   && signal.Value == 62);
        Assert.Contains(signals, signal => signal.MetricName == WellbeingMetrics.DistanceMetres && signal.Value == 6300.0);
    }

    [Fact]
    public async Task CollectSignalsAsync_OmitsHealthSignals_WhenHealthIsNotConnected()
    {
        _healthMock.SetupGet(provider => provider.IsConnected).Returns(false);

        _tasksMock.Setup(service => service.List(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<bool>())).Returns([]);
        _journalMock.Setup(service => service.ListEntries(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>())).Returns([]);
        _dailyRecordMock.Setup(service => service.GetByDate(TestDate)).Returns((DailyRecord?)null);

        var signals = await _collector.CollectSignalsAsync(TestDate);

        Assert.DoesNotContain(signals, signal => signal.Source == WellbeingSignalSource.Health);
    }

    [Fact]
    public async Task CollectSignalsAsync_OmitsHealthSignals_AndContinues_WhenHealthProviderThrows()
    {
        _healthMock.SetupGet(provider => provider.IsConnected).Returns(true);
        _healthMock
            .Setup(provider => provider.GetStepCountAsync(It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("phone unreachable"));

        _tasksMock.Setup(service => service.List(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<bool>())).Returns([]);
        _journalMock.Setup(service => service.ListEntries(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>())).Returns([]);
        _dailyRecordMock.Setup(service => service.GetByDate(TestDate)).Returns((DailyRecord?)null);

        var signals = await _collector.CollectSignalsAsync(TestDate);

        Assert.DoesNotContain(signals, signal => signal.Source == WellbeingSignalSource.Health);
        Assert.Contains(signals, signal => signal.Source == WellbeingSignalSource.DailyRecord);
    }

    // ====================================================================
    // Task signals
    // ====================================================================

    [Fact]
    public async Task CollectSignalsAsync_CountsTasksCompleted_OnDate()
    {
        _healthMock.SetupGet(provider => provider.IsConnected).Returns(false);
        _journalMock.Setup(service => service.ListEntries(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>())).Returns([]);
        _dailyRecordMock.Setup(service => service.GetByDate(TestDate)).Returns((DailyRecord?)null);

        var completedToday = MakeTask(completedAt: DayStart.AddHours(10));
        var completedYesterday = MakeTask(completedAt: DayStart.AddDays(-1));

        _tasksMock.Setup(service => service.List(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<bool>())).Returns([completedToday, completedYesterday]);

        var signals = await _collector.CollectSignalsAsync(TestDate);

        var completedSignal = Assert.Single(signals, signal => signal.MetricName == WellbeingMetrics.TasksCompleted);
        Assert.Equal(1.0, completedSignal.Value);
    }

    [Fact]
    public async Task CollectSignalsAsync_CountsTasksDue_OnDate()
    {
        _healthMock.SetupGet(provider => provider.IsConnected).Returns(false);
        _journalMock.Setup(service => service.ListEntries(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>())).Returns([]);
        _dailyRecordMock.Setup(service => service.GetByDate(TestDate)).Returns((DailyRecord?)null);

        var dueToday      = MakeTask(dueDate: DayStart.AddHours(12));
        var dueTomorrow   = MakeTask(dueDate: DayEnd.AddHours(6));
        var noDueDate     = MakeTask();

        _tasksMock.Setup(service => service.List(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<bool>())).Returns([dueToday, dueTomorrow, noDueDate]);

        var signals = await _collector.CollectSignalsAsync(TestDate);

        var dueSignal = Assert.Single(signals, signal => signal.MetricName == WellbeingMetrics.TasksDue);
        Assert.Equal(1.0, dueSignal.Value);
    }

    [Fact]
    public async Task CollectSignalsAsync_ComputesCompletionRate_WhenTasksDueOnDate()
    {
        _healthMock.SetupGet(provider => provider.IsConnected).Returns(false);
        _journalMock.Setup(service => service.ListEntries(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>())).Returns([]);
        _dailyRecordMock.Setup(service => service.GetByDate(TestDate)).Returns((DailyRecord?)null);

        var completedDueToday = MakeTask(dueDate: DayStart.AddHours(9) , completedAt: DayStart.AddHours(10));
        var notCompletedDueToday = MakeTask(dueDate: DayStart.AddHours(14));

        _tasksMock.Setup(service => service.List(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<bool>())).Returns([completedDueToday, notCompletedDueToday]);

        var signals = await _collector.CollectSignalsAsync(TestDate);

        var rateSignal = Assert.Single(signals, signal => signal.MetricName == WellbeingMetrics.TaskCompletionRate);
        Assert.Equal(0.5, rateSignal.Value, precision: 5);
        Assert.Equal("ratio", rateSignal.Unit);
    }

    [Fact]
    public async Task CollectSignalsAsync_OmitsCompletionRate_WhenNoTasksDueOnDate()
    {
        _healthMock.SetupGet(provider => provider.IsConnected).Returns(false);
        _journalMock.Setup(service => service.ListEntries(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>())).Returns([]);
        _dailyRecordMock.Setup(service => service.GetByDate(TestDate)).Returns((DailyRecord?)null);

        _tasksMock.Setup(service => service.List(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<bool>())).Returns([]);

        var signals = await _collector.CollectSignalsAsync(TestDate);

        Assert.DoesNotContain(signals, signal => signal.MetricName == WellbeingMetrics.TaskCompletionRate);
    }

    // ====================================================================
    // Journal signals
    // ====================================================================

    [Fact]
    public async Task CollectSignalsAsync_CountsJournalEntries_AndWords()
    {
        _healthMock.SetupGet(provider => provider.IsConnected).Returns(false);
        _tasksMock.Setup(service => service.List(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<bool>())).Returns([]);
        _dailyRecordMock.Setup(service => service.GetByDate(TestDate)).Returns((DailyRecord?)null);

        _journalMock
            .Setup(service => service.ListEntries(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>()))
            .Returns([MakeJournalEntry("Had a great day working"), MakeJournalEntry("Finished the report")]);

        var signals = await _collector.CollectSignalsAsync(TestDate);

        var entryCount = Assert.Single(signals, signal => signal.MetricName == WellbeingMetrics.JournalEntryCount);
        var wordCount  = Assert.Single(signals, signal => signal.MetricName == WellbeingMetrics.JournalWordCount);

        Assert.Equal(2.0, entryCount.Value);
        Assert.Equal(8.0, wordCount.Value);  // "Had a great day working" (5) + "Finished the report" (3) = 8
    }

    [Fact]
    public async Task CollectSignalsAsync_ReportsZeroWordCount_WhenNoJournalEntries()
    {
        _healthMock.SetupGet(provider => provider.IsConnected).Returns(false);
        _tasksMock.Setup(service => service.List(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<bool>())).Returns([]);
        _dailyRecordMock.Setup(service => service.GetByDate(TestDate)).Returns((DailyRecord?)null);

        _journalMock.Setup(service => service.ListEntries(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>())).Returns([]);

        var signals = await _collector.CollectSignalsAsync(TestDate);

        var wordCount  = Assert.Single(signals, signal => signal.MetricName == WellbeingMetrics.JournalWordCount);
        Assert.Equal(0.0, wordCount.Value);
    }

    // ====================================================================
    // DailyRecord signals
    // ====================================================================

    [Fact]
    public async Task CollectSignalsAsync_ReportsDayOpened_WhenDayIsOpen()
    {
        _healthMock.SetupGet(provider => provider.IsConnected).Returns(false);
        _tasksMock.Setup(service => service.List(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<bool>())).Returns([]);
        _journalMock.Setup(service => service.ListEntries(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>())).Returns([]);

        var record = new DailyRecord
                     {
                             Id         = TestDate.ToString("yyyy-MM-dd")
                           , Date       = TestDate
                           , OpenedAtUtc = DayStart.AddHours(7)
                     };
        _dailyRecordMock.Setup(service => service.GetByDate(TestDate)).Returns(record);

        var signals = await _collector.CollectSignalsAsync(TestDate);

        var opened = Assert.Single(signals, signal => signal.MetricName == WellbeingMetrics.DayOpened);
        var closed = Assert.Single(signals, signal => signal.MetricName == WellbeingMetrics.DayClosed);

        Assert.Equal(1.0, opened.Value);
        Assert.Equal(0.0, closed.Value);
    }

    [Fact]
    public async Task CollectSignalsAsync_ReportsBothOpenedAndClosed_WhenDayIsClosed()
    {
        _healthMock.SetupGet(provider => provider.IsConnected).Returns(false);
        _tasksMock.Setup(service => service.List(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<bool>())).Returns([]);
        _journalMock.Setup(service => service.ListEntries(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>())).Returns([]);

        var record = new DailyRecord
                     {
                             Id          = TestDate.ToString("yyyy-MM-dd")
                           , Date        = TestDate
                           , OpenedAtUtc = DayStart.AddHours(7)
                           , ClosedAtUtc = DayStart.AddHours(22)
                     };
        _dailyRecordMock.Setup(service => service.GetByDate(TestDate)).Returns(record);

        var signals = await _collector.CollectSignalsAsync(TestDate);

        var opened   = Assert.Single(signals, signal => signal.MetricName == WellbeingMetrics.DayOpened);
        var closed   = Assert.Single(signals, signal => signal.MetricName == WellbeingMetrics.DayClosed);
        var duration = Assert.Single(signals, signal => signal.MetricName == WellbeingMetrics.OpenDurationMinutes);

        Assert.Equal(1.0,   opened.Value);
        Assert.Equal(1.0,   closed.Value);
        Assert.Equal(900.0, duration.Value, precision: 5);  // 15 hours * 60 = 900 minutes
        Assert.Equal("minutes", duration.Unit);
    }

    [Fact]
    public async Task CollectSignalsAsync_ReportsDayNotOpened_WhenNoRecordExists()
    {
        _healthMock.SetupGet(provider => provider.IsConnected).Returns(false);
        _tasksMock.Setup(service => service.List(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<bool>())).Returns([]);
        _journalMock.Setup(service => service.ListEntries(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>())).Returns([]);
        _dailyRecordMock.Setup(service => service.GetByDate(TestDate)).Returns((DailyRecord?)null);

        var signals = await _collector.CollectSignalsAsync(TestDate);

        var opened = Assert.Single(signals, signal => signal.MetricName == WellbeingMetrics.DayOpened);
        var closed = Assert.Single(signals, signal => signal.MetricName == WellbeingMetrics.DayClosed);

        Assert.Equal(0.0, opened.Value);
        Assert.Equal(0.0, closed.Value);
        Assert.DoesNotContain(signals, signal => signal.MetricName == WellbeingMetrics.OpenDurationMinutes);
    }

    // ====================================================================
    // Persistence
    // ====================================================================

    [Fact]
    public async Task CollectSignalsAsync_SavesEachSignal_ToStore()
    {
        _healthMock.SetupGet(provider => provider.IsConnected).Returns(false);
        _tasksMock.Setup(service => service.List(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<bool>())).Returns([]);
        _journalMock.Setup(service => service.ListEntries(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>())).Returns([]);
        _dailyRecordMock.Setup(service => service.GetByDate(TestDate)).Returns((DailyRecord?)null);

        var signals = await _collector.CollectSignalsAsync(TestDate);

        _storeMock.Verify(
            store => store.SaveSignalAsync(It.IsAny<WellbeingSignal>(), It.IsAny<CancellationToken>())
          , Times.Exactly(signals.Count));
    }

    // ====================================================================
    // Signal properties
    // ====================================================================

    [Fact]
    public async Task CollectSignalsAsync_Signals_HaveCorrectSource_AndDate()
    {
        _healthMock.SetupGet(provider => provider.IsConnected).Returns(false);
        _tasksMock.Setup(service => service.List(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<bool>())).Returns([]);
        _journalMock.Setup(service => service.ListEntries(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>())).Returns([]);
        _dailyRecordMock.Setup(service => service.GetByDate(TestDate)).Returns((DailyRecord?)null);

        var signals = await _collector.CollectSignalsAsync(TestDate);

        Assert.All(signals, signal => Assert.Equal(TestDate, signal.Date));

        Assert.All(signals.Where(signal => signal.MetricName is WellbeingMetrics.TasksCompleted
                                                              or WellbeingMetrics.TasksDue)
                 , signal => Assert.Equal(WellbeingSignalSource.Tasks, signal.Source));

        Assert.All(signals.Where(signal => signal.MetricName is WellbeingMetrics.JournalEntryCount
                                                              or WellbeingMetrics.JournalWordCount)
                 , signal => Assert.Equal(WellbeingSignalSource.Journal, signal.Source));
    }
}
