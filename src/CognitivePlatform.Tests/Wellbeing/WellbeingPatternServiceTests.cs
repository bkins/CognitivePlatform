using CognitivePlatform.Api.Wellbeing;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace CognitivePlatform.Tests.Wellbeing;

public sealed class WellbeingPatternServiceTests
{
    private readonly Mock<IWellbeingSignalStore>  _storeMock = new();
    private readonly WellbeingPatternService      _service;

    private static readonly DateOnly TestFrom = new(2026, 5, 23);
    private static readonly DateOnly TestTo   = new(2026, 5, 29);

    public WellbeingPatternServiceTests()
    {
        _service = new WellbeingPatternService(
            _storeMock.Object
          , NullLogger<WellbeingPatternService>.Instance);
    }

    // ====================================================================
    // Minimum data guard
    // ====================================================================

    [Fact]
    public async Task AnalyseAsync_ReturnsNotEnoughData_WhenFewerThanThreeDaysOfSignals()
    {
        SetupSignals(BuildSignalsForDays(2, steps: 8000, sleepMinutes: 420, taskRate: 0.8));

        var report = await _service.AnalyseAsync(TestFrom, TestTo);

        Assert.Empty(report.Patterns);
        Assert.Contains("Not enough data", report.NarrativeSummary);
    }

    [Fact]
    public async Task AnalyseAsync_ProceedsToDetection_WhenThreeOrMoreDaysOfSignalsPresent()
    {
        SetupSignals(BuildSignalsForDays(4, steps: 8000, sleepMinutes: 480, taskRate: 0.9));

        var report = await _service.AnalyseAsync(TestFrom, TestTo);

        Assert.NotNull(report);
        Assert.NotEmpty(report.NarrativeSummary);
    }

    // ====================================================================
    // Rule 1 — Sleep–task correlation
    // ====================================================================

    [Fact]
    public async Task AnalyseAsync_DetectsSleepTaskCorrelation_WhenSleepLowAndTaskRateLow()
    {
        // avg sleep = 5h (< 6h threshold), task rate = 0.60 (< 70% threshold)
        SetupSignals(BuildSignalsForDays(4, steps: 8000, sleepMinutes: 300, taskRate: 0.60));

        var report = await _service.AnalyseAsync(TestFrom, TestTo);

        var pattern = report.Patterns.SingleOrDefault(p => p.Name == "SleepTaskCorrelation");
        Assert.NotNull(pattern);
        Assert.Equal(PatternSeverity.Concern, pattern.Severity);
        Assert.Equal(2, pattern.Sources.Length);
        Assert.Contains(WellbeingSignalSource.Health, pattern.Sources);
        Assert.Contains(WellbeingSignalSource.Tasks,  pattern.Sources);
    }

    [Fact]
    public async Task AnalyseAsync_DoesNotDetectSleepTaskCorrelation_WhenSleepIsAdequate()
    {
        // avg sleep = 7h (above 6h threshold)
        SetupSignals(BuildSignalsForDays(4, steps: 8000, sleepMinutes: 420, taskRate: 0.50));

        var report = await _service.AnalyseAsync(TestFrom, TestTo);

        Assert.DoesNotContain(report.Patterns, p => p.Name == "SleepTaskCorrelation");
    }

    [Fact]
    public async Task AnalyseAsync_DoesNotDetectSleepTaskCorrelation_WhenTaskRateIsAdequate()
    {
        // task rate = 0.80 (above 70% threshold)
        SetupSignals(BuildSignalsForDays(4, steps: 8000, sleepMinutes: 300, taskRate: 0.80));

        var report = await _service.AnalyseAsync(TestFrom, TestTo);

        Assert.DoesNotContain(report.Patterns, p => p.Name == "SleepTaskCorrelation");
    }

    [Fact]
    public async Task AnalyseAsync_SleepTaskCorrelation_IncludesCorrelation_WhenPairedDataAvailable()
    {
        // Vary sleep and task values across days to ensure non-zero variance for Pearson calculation.
        var signals = new List<WellbeingSignal>
        {
            Signal(TestFrom,            WellbeingSignalSource.Health, WellbeingMetrics.SleepMinutes,       300),
            Signal(TestFrom,            WellbeingSignalSource.Tasks,  WellbeingMetrics.TaskCompletionRate, 0.60),
            Signal(TestFrom,            WellbeingSignalSource.Health, WellbeingMetrics.Steps,              8000),
            Signal(TestFrom.AddDays(1), WellbeingSignalSource.Health, WellbeingMetrics.SleepMinutes,       240),
            Signal(TestFrom.AddDays(1), WellbeingSignalSource.Tasks,  WellbeingMetrics.TaskCompletionRate, 0.40),
            Signal(TestFrom.AddDays(1), WellbeingSignalSource.Health, WellbeingMetrics.Steps,              8000),
            Signal(TestFrom.AddDays(2), WellbeingSignalSource.Health, WellbeingMetrics.SleepMinutes,       330),
            Signal(TestFrom.AddDays(2), WellbeingSignalSource.Tasks,  WellbeingMetrics.TaskCompletionRate, 0.55),
            Signal(TestFrom.AddDays(2), WellbeingSignalSource.Health, WellbeingMetrics.Steps,              8000),
            Signal(TestFrom.AddDays(3), WellbeingSignalSource.Health, WellbeingMetrics.SleepMinutes,       270),
            Signal(TestFrom.AddDays(3), WellbeingSignalSource.Tasks,  WellbeingMetrics.TaskCompletionRate, 0.45),
            Signal(TestFrom.AddDays(3), WellbeingSignalSource.Health, WellbeingMetrics.Steps,              8000),
        };
        SetupSignals(signals);

        var report = await _service.AnalyseAsync(TestFrom, TestTo);

        var pattern = report.Patterns.Single(p => p.Name == "SleepTaskCorrelation");
        Assert.NotNull(pattern.Correlation);
        Assert.InRange(pattern.Correlation.Value, -1.0, 1.0);
    }

    // ====================================================================
    // Rule 2 — Step count trend
    // ====================================================================

    [Fact]
    public async Task AnalyseAsync_DetectsPositiveStepTrend_WhenCurrentStepsMoreThan10PctHigher()
    {
        var currentSignals  = BuildSignalsForDays(4, steps: 12000, sleepMinutes: 480, taskRate: 0.9);
        var previousSignals = BuildSignalsForDays(4, steps: 8000,  sleepMinutes: 480, taskRate: 0.9);
        SetupSignalsWithPrevious(currentSignals, previousSignals);

        var report = await _service.AnalyseAsync(TestFrom, TestTo);

        var pattern = report.Patterns.SingleOrDefault(p => p.Name == "StepCountTrend");
        Assert.NotNull(pattern);
        Assert.Equal(PatternSeverity.Positive, pattern.Severity);
    }

    [Fact]
    public async Task AnalyseAsync_DetectsAttentionStepTrend_WhenCurrentStepsMoreThan20PctLower()
    {
        var currentSignals  = BuildSignalsForDays(4, steps: 5000,  sleepMinutes: 480, taskRate: 0.9);
        var previousSignals = BuildSignalsForDays(4, steps: 10000, sleepMinutes: 480, taskRate: 0.9);
        SetupSignalsWithPrevious(currentSignals, previousSignals);

        var report = await _service.AnalyseAsync(TestFrom, TestTo);

        var pattern = report.Patterns.SingleOrDefault(p => p.Name == "StepCountTrend");
        Assert.NotNull(pattern);
        Assert.Equal(PatternSeverity.Attention, pattern.Severity);
    }

    [Fact]
    public async Task AnalyseAsync_DoesNotDetectStepTrend_WhenChangeIsMinor()
    {
        // 5% increase — below the 10% positive threshold
        var currentSignals  = BuildSignalsForDays(4, steps: 10500, sleepMinutes: 480, taskRate: 0.9);
        var previousSignals = BuildSignalsForDays(4, steps: 10000, sleepMinutes: 480, taskRate: 0.9);
        SetupSignalsWithPrevious(currentSignals, previousSignals);

        var report = await _service.AnalyseAsync(TestFrom, TestTo);

        Assert.DoesNotContain(report.Patterns, p => p.Name == "StepCountTrend");
    }

    [Fact]
    public async Task AnalyseAsync_DoesNotDetectStepTrend_WhenNoPreviousPeriodData()
    {
        SetupSignalsWithPrevious(
            BuildSignalsForDays(4, steps: 8000, sleepMinutes: 480, taskRate: 0.9)
          , previousSignals: []);

        var report = await _service.AnalyseAsync(TestFrom, TestTo);

        Assert.DoesNotContain(report.Patterns, p => p.Name == "StepCountTrend");
    }

    // ====================================================================
    // Rule 3 — Journal engagement drop
    // ====================================================================

    [Fact]
    public async Task AnalyseAsync_DetectsJournalEngagementDrop_WhenFewEntriesButDaysOpenedAndClosed()
    {
        var signals = BuildSignalsForDays(4, steps: 8000, sleepMinutes: 480, taskRate: 0.9);
        signals.AddRange(BuildDailyRecordSignals(4, opened: true, closed: true));
        // Only 1 journal entry across the whole period
        signals.AddRange(BuildJournalSignals(4, entryCount: 0));
        signals.Add(Signal(TestFrom, WellbeingSignalSource.Journal, WellbeingMetrics.JournalEntryCount, 1));
        SetupSignals(signals);

        var report = await _service.AnalyseAsync(TestFrom, TestTo);

        Assert.Contains(report.Patterns, p => p.Name == "JournalEngagementDrop");
    }

    [Fact]
    public async Task AnalyseAsync_DoesNotDetectJournalEngagement_WhenEnoughJournalEntries()
    {
        var signals = BuildSignalsForDays(4, steps: 8000, sleepMinutes: 480, taskRate: 0.9);
        signals.AddRange(BuildDailyRecordSignals(4, opened: true, closed: true));
        // 4 entries (>= 3 threshold)
        signals.AddRange(BuildJournalSignals(4, entryCount: 1));
        SetupSignals(signals);

        var report = await _service.AnalyseAsync(TestFrom, TestTo);

        Assert.DoesNotContain(report.Patterns, p => p.Name == "JournalEngagementDrop");
    }

    [Fact]
    public async Task AnalyseAsync_DoesNotDetectJournalEngagement_WhenDaysNotConsistentlyOpenedAndClosed()
    {
        var signals = BuildSignalsForDays(4, steps: 8000, sleepMinutes: 480, taskRate: 0.9);
        // No DailyRecord signals — days not opened/closed
        signals.AddRange(BuildJournalSignals(4, entryCount: 0));
        SetupSignals(signals);

        var report = await _service.AnalyseAsync(TestFrom, TestTo);

        Assert.DoesNotContain(report.Patterns, p => p.Name == "JournalEngagementDrop");
    }

    // ====================================================================
    // Rule 4 — Rest day detection
    // ====================================================================

    [Fact]
    public async Task AnalyseAsync_DetectsRestDay_WhenStepsBelowThresholdAndNoTasksCompleted()
    {
        // Build 3 normal days at indexes 1-3, plus a rest day at index 0 (TestFrom).
        // Using separate dates avoids signal key conflicts when two values share the same metric.
        var signals = BuildSignalsForDays(3, steps: 8000, sleepMinutes: 480, taskRate: 0.9, startOffset: 1);
        signals.Add(Signal(TestFrom, WellbeingSignalSource.Health, WellbeingMetrics.Steps,          1000));
        signals.Add(Signal(TestFrom, WellbeingSignalSource.Tasks,  WellbeingMetrics.TasksCompleted, 0));
        SetupSignals(signals);

        var report = await _service.AnalyseAsync(TestFrom, TestTo);

        Assert.Contains(report.Patterns, p => p.Name == "RestDayDetected");
    }

    [Fact]
    public async Task AnalyseAsync_DoesNotDetectRestDay_WhenStepsAboveThreshold()
    {
        SetupSignals(BuildSignalsForDays(4, steps: 5000, sleepMinutes: 480, taskRate: 0.9));

        var report = await _service.AnalyseAsync(TestFrom, TestTo);

        Assert.DoesNotContain(report.Patterns, p => p.Name == "RestDayDetected");
    }

    [Fact]
    public async Task AnalyseAsync_RestDayPattern_HasNeutralSeverity()
    {
        // 3 normal days at indexes 1-3, plus a dedicated rest day at index 0.
        var signals = BuildSignalsForDays(3, steps: 8000, sleepMinutes: 480, taskRate: 0.9, startOffset: 1);
        signals.Add(Signal(TestFrom, WellbeingSignalSource.Health, WellbeingMetrics.Steps,          500));
        signals.Add(Signal(TestFrom, WellbeingSignalSource.Tasks,  WellbeingMetrics.TasksCompleted, 0));
        SetupSignals(signals);

        var report = await _service.AnalyseAsync(TestFrom, TestTo);

        var pattern = report.Patterns.SingleOrDefault(p => p.Name == "RestDayDetected");
        Assert.NotNull(pattern);
        Assert.Equal(PatternSeverity.Neutral, pattern.Severity);
    }

    // ====================================================================
    // Narrative summary
    // ====================================================================

    [Fact]
    public async Task AnalyseAsync_NarrativeSummary_IsNonEmpty_WhenPatternsDetected()
    {
        SetupSignals(BuildSignalsForDays(4, steps: 8000, sleepMinutes: 300, taskRate: 0.60));

        var report = await _service.AnalyseAsync(TestFrom, TestTo);

        Assert.NotEmpty(report.NarrativeSummary);
    }

    [Fact]
    public async Task AnalyseAsync_NarrativeSummary_IsNonEmpty_WhenNoPatterns()
    {
        SetupSignals(BuildSignalsForDays(4, steps: 8000, sleepMinutes: 480, taskRate: 0.9));

        var report = await _service.AnalyseAsync(TestFrom, TestTo);

        Assert.NotEmpty(report.NarrativeSummary);
    }

    [Fact]
    public async Task AnalyseAsync_NarrativeSummary_MentionsSleep_WhenSleepTaskCorrelationDetected()
    {
        SetupSignals(BuildSignalsForDays(4, steps: 8000, sleepMinutes: 300, taskRate: 0.60));

        var report = await _service.AnalyseAsync(TestFrom, TestTo);

        Assert.Contains("sleep", report.NarrativeSummary, StringComparison.OrdinalIgnoreCase);
    }

    // ====================================================================
    // Report shape
    // ====================================================================

    [Fact]
    public async Task AnalyseAsync_ReturnsCorrectFromAndTo()
    {
        SetupSignals(BuildSignalsForDays(4, steps: 8000, sleepMinutes: 480, taskRate: 0.9));

        var report = await _service.AnalyseAsync(TestFrom, TestTo);

        Assert.Equal(TestFrom, report.From);
        Assert.Equal(TestTo,   report.To);
    }

    // ====================================================================
    // Test helpers
    // ====================================================================

    private void SetupSignals(IReadOnlyList<WellbeingSignal> signals)
    {
        _storeMock
            .Setup(store => store.GetSignalsAsync(
                It.IsAny<DateTimeOffset>()
              , It.IsAny<DateTimeOffset>()
              , It.IsAny<CancellationToken>()))
            .ReturnsAsync(signals);
    }

    private void SetupSignalsWithPrevious(
        IReadOnlyList<WellbeingSignal> currentSignals
      , IReadOnlyList<WellbeingSignal> previousSignals)
    {
        _storeMock
            .SetupSequence(store => store.GetSignalsAsync(
                It.IsAny<DateTimeOffset>()
              , It.IsAny<DateTimeOffset>()
              , It.IsAny<CancellationToken>()))
            .ReturnsAsync(currentSignals)
            .ReturnsAsync(previousSignals);
    }

    private static List<WellbeingSignal> BuildSignalsForDays(
        int    dayCount
      , double steps
      , double sleepMinutes
      , double taskRate
      , int    startOffset = 0)
    {
        var signals = new List<WellbeingSignal>();

        for (var i = 0; i < dayCount; i++)
        {
            var date = TestFrom.AddDays(startOffset + i);
            signals.Add(Signal(date, WellbeingSignalSource.Health, WellbeingMetrics.Steps,              steps));
            signals.Add(Signal(date, WellbeingSignalSource.Health, WellbeingMetrics.SleepMinutes,       sleepMinutes));
            signals.Add(Signal(date, WellbeingSignalSource.Tasks,  WellbeingMetrics.TaskCompletionRate, taskRate));
            signals.Add(Signal(date, WellbeingSignalSource.Tasks,  WellbeingMetrics.TasksCompleted,     3));
        }

        return signals;
    }

    private static List<WellbeingSignal> BuildDailyRecordSignals(int dayCount, bool opened, bool closed)
    {
        var signals = new List<WellbeingSignal>();

        for (var i = 0; i < dayCount; i++)
        {
            var date = TestFrom.AddDays(i);
            signals.Add(Signal(date, WellbeingSignalSource.DailyRecord, WellbeingMetrics.DayOpened, opened ? 1.0 : 0.0));
            signals.Add(Signal(date, WellbeingSignalSource.DailyRecord, WellbeingMetrics.DayClosed, closed ? 1.0 : 0.0));
        }

        return signals;
    }

    private static List<WellbeingSignal> BuildJournalSignals(int dayCount, int entryCount)
    {
        var signals = new List<WellbeingSignal>();

        for (var i = 0; i < dayCount; i++)
        {
            var date = TestFrom.AddDays(i);
            signals.Add(Signal(date, WellbeingSignalSource.Journal, WellbeingMetrics.JournalEntryCount, entryCount));
        }

        return signals;
    }

    private static WellbeingSignal Signal(
        DateOnly              date
      , WellbeingSignalSource source
      , string                metricName
      , double                value) =>
        new()
        {
            Id          = $"{source}.{metricName}.{date:yyyy-MM-dd}"
          , Date        = date
          , Source      = source
          , MetricName  = metricName
          , Value       = value
          , CollectedAt = DateTimeOffset.UtcNow
        };
}
