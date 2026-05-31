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
    // W.2 Rule 5 — Weekly activity trend (steps + distance both down >15%)
    // ====================================================================

    [Fact]
    public async Task AnalyseAsync_DetectsWeeklyActivityTrend_WhenBothStepsAndDistanceDownMoreThan15Pct()
    {
        var currentSignals  = BuildSignalsForDays(4, steps: 8000, sleepMinutes: 480, taskRate: 0.9);
        var previousSignals = BuildSignalsForDays(4, steps: 10000, sleepMinutes: 480, taskRate: 0.9);
        AddDistanceSignals(currentSignals,  4, distanceMetres: 5000);
        AddDistanceSignals(previousSignals, 4, distanceMetres: 7000);
        SetupSignalsWithPrevious(currentSignals, previousSignals);

        var report = await _service.AnalyseAsync(TestFrom, TestTo);

        var pattern = report.Patterns.SingleOrDefault(p => p.Name == "WeeklyActivityTrend");
        Assert.NotNull(pattern);
        Assert.Equal(PatternSeverity.Attention, pattern.Severity);
    }

    [Fact]
    public async Task AnalyseAsync_DoesNotDetectWeeklyActivityTrend_WhenOnlyStepsAreDown()
    {
        // Steps down 20% but distance unchanged — both must drop >15% to fire
        var currentSignals  = BuildSignalsForDays(4, steps: 8000, sleepMinutes: 480, taskRate: 0.9);
        var previousSignals = BuildSignalsForDays(4, steps: 10000, sleepMinutes: 480, taskRate: 0.9);
        AddDistanceSignals(currentSignals,  4, distanceMetres: 7000);
        AddDistanceSignals(previousSignals, 4, distanceMetres: 7000);
        SetupSignalsWithPrevious(currentSignals, previousSignals);

        var report = await _service.AnalyseAsync(TestFrom, TestTo);

        Assert.DoesNotContain(report.Patterns, p => p.Name == "WeeklyActivityTrend");
    }

    [Fact]
    public async Task AnalyseAsync_DoesNotDetectWeeklyActivityTrend_WhenNoPreviousDistanceData()
    {
        var currentSignals  = BuildSignalsForDays(4, steps: 8000, sleepMinutes: 480, taskRate: 0.9);
        var previousSignals = BuildSignalsForDays(4, steps: 10000, sleepMinutes: 480, taskRate: 0.9);
        AddDistanceSignals(currentSignals, 4, distanceMetres: 5000);
        // No distance in previous signals — guard returns null
        SetupSignalsWithPrevious(currentSignals, previousSignals);

        var report = await _service.AnalyseAsync(TestFrom, TestTo);

        Assert.DoesNotContain(report.Patterns, p => p.Name == "WeeklyActivityTrend");
    }

    // ====================================================================
    // W.2 Rule 6 — Sleep consistency (population stddev > 90 min)
    // ====================================================================

    [Fact]
    public async Task AnalyseAsync_DetectsSleepConsistency_WhenStdDevExceeds90Minutes()
    {
        // Values 240, 300, 360, 480, 540 → stddev ≈ 111 min > 90
        var signals = new List<WellbeingSignal>();
        var sleepValues = new[] { 240.0, 300.0, 360.0, 480.0, 540.0 };
        for (var i = 0; i < sleepValues.Length; i++)
        {
            var date = TestFrom.AddDays(i);
            signals.Add(Signal(date, WellbeingSignalSource.Health, WellbeingMetrics.Steps,              8000));
            signals.Add(Signal(date, WellbeingSignalSource.Health, WellbeingMetrics.SleepMinutes,       sleepValues[i]));
            signals.Add(Signal(date, WellbeingSignalSource.Tasks,  WellbeingMetrics.TaskCompletionRate, 0.9));
            signals.Add(Signal(date, WellbeingSignalSource.Tasks,  WellbeingMetrics.TasksCompleted,     3));
        }
        SetupSignals(signals);

        var report = await _service.AnalyseAsync(TestFrom, TestTo);

        var pattern = report.Patterns.SingleOrDefault(p => p.Name == "SleepConsistency");
        Assert.NotNull(pattern);
        Assert.Equal(PatternSeverity.Attention, pattern.Severity);
    }

    [Fact]
    public async Task AnalyseAsync_DoesNotDetectSleepConsistency_WhenSleepIsUniform()
    {
        // Same sleep every day → stddev = 0
        SetupSignals(BuildSignalsForDays(5, steps: 8000, sleepMinutes: 420, taskRate: 0.9));

        var report = await _service.AnalyseAsync(TestFrom, TestTo);

        Assert.DoesNotContain(report.Patterns, p => p.Name == "SleepConsistency");
    }

    // ====================================================================
    // W.2 Rule 7 — Journal word count drop (<50% of previous period avg)
    // ====================================================================

    [Fact]
    public async Task AnalyseAsync_DetectsJournalWordCountDrop_WhenCurrentWordCountBelow50PctOfPrevious()
    {
        // 4 entries this week (>3 threshold); avg 100 words vs prev avg 300 → ratio 0.33 < 0.50
        var currentSignals  = BuildSignalsForDays(4, steps: 8000, sleepMinutes: 480, taskRate: 0.9);
        var previousSignals = BuildSignalsForDays(4, steps: 8000, sleepMinutes: 480, taskRate: 0.9);
        currentSignals.AddRange(BuildJournalWordSignals(4, entryCount: 1, wordCount: 100));
        previousSignals.AddRange(BuildJournalWordSignals(4, entryCount: 1, wordCount: 300));
        SetupSignalsWithPrevious(currentSignals, previousSignals);

        var report = await _service.AnalyseAsync(TestFrom, TestTo);

        var pattern = report.Patterns.SingleOrDefault(p => p.Name == "JournalWordCountDrop");
        Assert.NotNull(pattern);
        Assert.Equal(PatternSeverity.Attention, pattern.Severity);
    }

    [Fact]
    public async Task AnalyseAsync_DoesNotDetectJournalWordCountDrop_WhenRatioIsAbove50Pct()
    {
        // avg 200 words vs prev avg 300 → ratio 0.67 ≥ 0.50
        var currentSignals  = BuildSignalsForDays(4, steps: 8000, sleepMinutes: 480, taskRate: 0.9);
        var previousSignals = BuildSignalsForDays(4, steps: 8000, sleepMinutes: 480, taskRate: 0.9);
        currentSignals.AddRange(BuildJournalWordSignals(4, entryCount: 1, wordCount: 200));
        previousSignals.AddRange(BuildJournalWordSignals(4, entryCount: 1, wordCount: 300));
        SetupSignalsWithPrevious(currentSignals, previousSignals);

        var report = await _service.AnalyseAsync(TestFrom, TestTo);

        Assert.DoesNotContain(report.Patterns, p => p.Name == "JournalWordCountDrop");
    }

    [Fact]
    public async Task AnalyseAsync_DoesNotDetectJournalWordCountDrop_WhenJournalEntryCountIsThreeOrFewer()
    {
        // Only 3 total entries (not >3) — proxy guard suppresses the rule
        var currentSignals  = BuildSignalsForDays(4, steps: 8000, sleepMinutes: 480, taskRate: 0.9);
        var previousSignals = BuildSignalsForDays(4, steps: 8000, sleepMinutes: 480, taskRate: 0.9);
        currentSignals.Add(Signal(TestFrom, WellbeingSignalSource.Journal, WellbeingMetrics.JournalEntryCount, 3));
        currentSignals.Add(Signal(TestFrom, WellbeingSignalSource.Journal, WellbeingMetrics.JournalWordCount,  100));
        previousSignals.AddRange(BuildJournalWordSignals(4, entryCount: 1, wordCount: 300));
        SetupSignalsWithPrevious(currentSignals, previousSignals);

        var report = await _service.AnalyseAsync(TestFrom, TestTo);

        Assert.DoesNotContain(report.Patterns, p => p.Name == "JournalWordCountDrop");
    }

    // ====================================================================
    // W.2 Rule 8 — Good recovery (≥2 rest days, no Concern patterns)
    // ====================================================================

    [Fact]
    public async Task AnalyseAsync_DetectsGoodRecovery_WhenTwoOrMoreRestDaysAndNoConcernPatterns()
    {
        // 3 active days (offset 2+) + 2 rest days at offsets 0 and 1 (steps < 2000)
        // Adequate sleep and task rate so no Concern pattern fires
        var signals = BuildSignalsForDays(3, steps: 8000, sleepMinutes: 480, taskRate: 0.9, startOffset: 2);
        signals.Add(Signal(TestFrom,            WellbeingSignalSource.Health, WellbeingMetrics.Steps,              1500));
        signals.Add(Signal(TestFrom,            WellbeingSignalSource.Health, WellbeingMetrics.SleepMinutes,       480));
        signals.Add(Signal(TestFrom,            WellbeingSignalSource.Tasks,  WellbeingMetrics.TaskCompletionRate, 0.9));
        signals.Add(Signal(TestFrom.AddDays(1), WellbeingSignalSource.Health, WellbeingMetrics.Steps,              1200));
        signals.Add(Signal(TestFrom.AddDays(1), WellbeingSignalSource.Health, WellbeingMetrics.SleepMinutes,       480));
        signals.Add(Signal(TestFrom.AddDays(1), WellbeingSignalSource.Tasks,  WellbeingMetrics.TaskCompletionRate, 0.9));
        SetupSignals(signals);

        var report = await _service.AnalyseAsync(TestFrom, TestTo);

        var pattern = report.Patterns.SingleOrDefault(p => p.Name == "GoodRecovery");
        Assert.NotNull(pattern);
        Assert.Equal(PatternSeverity.Positive, pattern.Severity);
    }

    [Fact]
    public async Task AnalyseAsync_DoesNotDetectGoodRecovery_WhenConcernPatternExists()
    {
        // Low sleep + low task rate → SleepTaskCorrelation (Concern) fires; suppresses GoodRecovery
        var signals = BuildSignalsForDays(3, steps: 8000, sleepMinutes: 300, taskRate: 0.60, startOffset: 2);
        signals.Add(Signal(TestFrom,            WellbeingSignalSource.Health, WellbeingMetrics.Steps,              1500));
        signals.Add(Signal(TestFrom,            WellbeingSignalSource.Health, WellbeingMetrics.SleepMinutes,       300));
        signals.Add(Signal(TestFrom,            WellbeingSignalSource.Tasks,  WellbeingMetrics.TaskCompletionRate, 0.60));
        signals.Add(Signal(TestFrom.AddDays(1), WellbeingSignalSource.Health, WellbeingMetrics.Steps,              1200));
        signals.Add(Signal(TestFrom.AddDays(1), WellbeingSignalSource.Health, WellbeingMetrics.SleepMinutes,       300));
        signals.Add(Signal(TestFrom.AddDays(1), WellbeingSignalSource.Tasks,  WellbeingMetrics.TaskCompletionRate, 0.60));
        SetupSignals(signals);

        var report = await _service.AnalyseAsync(TestFrom, TestTo);

        Assert.Contains(report.Patterns,    p => p.Severity == PatternSeverity.Concern);
        Assert.DoesNotContain(report.Patterns, p => p.Name == "GoodRecovery");
    }

    [Fact]
    public async Task AnalyseAsync_DoesNotDetectGoodRecovery_WhenFewerThanTwoRestDays()
    {
        // Only 1 day with steps < 2000
        var signals = BuildSignalsForDays(4, steps: 8000, sleepMinutes: 480, taskRate: 0.9, startOffset: 1);
        signals.Add(Signal(TestFrom, WellbeingSignalSource.Health, WellbeingMetrics.Steps,              1500));
        signals.Add(Signal(TestFrom, WellbeingSignalSource.Health, WellbeingMetrics.SleepMinutes,       480));
        signals.Add(Signal(TestFrom, WellbeingSignalSource.Tasks,  WellbeingMetrics.TaskCompletionRate, 0.9));
        SetupSignals(signals);

        var report = await _service.AnalyseAsync(TestFrom, TestTo);

        Assert.DoesNotContain(report.Patterns, p => p.Name == "GoodRecovery");
    }

    // ====================================================================
    // W.2 Rule 9 — Open/close streak (≥5 consecutive days)
    // ====================================================================

    [Fact]
    public async Task AnalyseAsync_DetectsOpenCloseStreak_WhenFiveConsecutiveDaysOpenedAndClosed()
    {
        var signals = BuildSignalsForDays(5, steps: 8000, sleepMinutes: 480, taskRate: 0.9);
        signals.AddRange(BuildDailyRecordSignals(5, opened: true, closed: true));
        SetupSignals(signals);

        var report = await _service.AnalyseAsync(TestFrom, TestTo);

        var pattern = report.Patterns.SingleOrDefault(p => p.Name == "OpenCloseStreak");
        Assert.NotNull(pattern);
        Assert.Equal(PatternSeverity.Positive, pattern.Severity);
    }

    [Fact]
    public async Task AnalyseAsync_DoesNotDetectOpenCloseStreak_WhenOnlyFourConsecutiveDays()
    {
        var signals = BuildSignalsForDays(4, steps: 8000, sleepMinutes: 480, taskRate: 0.9);
        signals.AddRange(BuildDailyRecordSignals(4, opened: true, closed: true));
        SetupSignals(signals);

        var report = await _service.AnalyseAsync(TestFrom, TestTo);

        Assert.DoesNotContain(report.Patterns, p => p.Name == "OpenCloseStreak");
    }

    [Fact]
    public async Task AnalyseAsync_DoesNotDetectOpenCloseStreak_WhenStreakIsBrokenBeforeFiveDays()
    {
        // 7 days total: days 0-2 open+close, day 3 no open/close (gap), days 4-6 open+close
        // Max streak = 3, which is < 5
        var signals = BuildSignalsForDays(7, steps: 8000, sleepMinutes: 480, taskRate: 0.9);
        for (var i = 0; i < 7; i++)
        {
            if (i == 3) continue;
            var date = TestFrom.AddDays(i);
            signals.Add(Signal(date, WellbeingSignalSource.DailyRecord, WellbeingMetrics.DayOpened, 1.0));
            signals.Add(Signal(date, WellbeingSignalSource.DailyRecord, WellbeingMetrics.DayClosed, 1.0));
        }
        SetupSignals(signals);

        var report = await _service.AnalyseAsync(TestFrom, TestTo);

        Assert.DoesNotContain(report.Patterns, p => p.Name == "OpenCloseStreak");
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

    private static void AddDistanceSignals(List<WellbeingSignal> signals, int dayCount, double distanceMetres, int startOffset = 0)
    {
        for (var i = 0; i < dayCount; i++)
            signals.Add(Signal(TestFrom.AddDays(startOffset + i), WellbeingSignalSource.Health, WellbeingMetrics.DistanceMetres, distanceMetres));
    }

    private static List<WellbeingSignal> BuildJournalWordSignals(int dayCount, int entryCount, double wordCount)
    {
        var signals = new List<WellbeingSignal>();
        for (var i = 0; i < dayCount; i++)
        {
            var date = TestFrom.AddDays(i);
            signals.Add(Signal(date, WellbeingSignalSource.Journal, WellbeingMetrics.JournalEntryCount, entryCount));
            signals.Add(Signal(date, WellbeingSignalSource.Journal, WellbeingMetrics.JournalWordCount,  wordCount));
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
