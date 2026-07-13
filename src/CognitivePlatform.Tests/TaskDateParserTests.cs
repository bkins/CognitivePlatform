using CognitivePlatform.Api.Domains.Tasks;

namespace CognitivePlatform.Tests;

public class TaskDateParserTests
{
    // ================================================================
    // TryParseDate — keyword tokens
    // ================================================================

    [Fact]
    public void TryParseDate_ReturnsToday_ForTodayToken()
    {
        var result = TaskDateParser.TryParseDate("today", out var value);

        Assert.True(result);
        Assert.Equal(DateTimeOffset.UtcNow.Date, value.Date);
    }

    [Fact]
    public void TryParseDate_ReturnsTomorrow_ForTomorrowToken()
    {
        var result = TaskDateParser.TryParseDate("tomorrow", out var value);

        Assert.True(result);
        Assert.Equal(DateTimeOffset.UtcNow.Date.AddDays(1), value.Date);
    }

    [Fact]
    public void TryParseDate_ReturnsFalse_ForNullInput()
    {
        var result = TaskDateParser.TryParseDate(null, out _);

        Assert.False(result);
    }

    [Fact]
    public void TryParseDate_ReturnsFalse_ForEmptyInput()
    {
        var result = TaskDateParser.TryParseDate(string.Empty, out _);

        Assert.False(result);
    }

    [Fact]
    public void TryParseDate_ReturnsFalse_ForUnrecognisedText()
    {
        var result = TaskDateParser.TryParseDate("soon-ish", out _);

        Assert.False(result);
    }

    [Theory]
    [InlineData("monday",    DayOfWeek.Monday)]
    [InlineData("mon",       DayOfWeek.Monday)]
    [InlineData("tuesday",   DayOfWeek.Tuesday)]
    [InlineData("wednesday", DayOfWeek.Wednesday)]
    [InlineData("thursday",  DayOfWeek.Thursday)]
    [InlineData("friday",    DayOfWeek.Friday)]
    [InlineData("saturday",  DayOfWeek.Saturday)]
    [InlineData("sunday",    DayOfWeek.Sunday)]
    public void TryParseDate_ReturnsNextOccurrence_ForWeekdayToken(string token, DayOfWeek expected)
    {
        var result = TaskDateParser.TryParseDate(token, out var value);

        Assert.True(result);
        Assert.Equal(expected, value.DayOfWeek);
    }

    [Theory]
    [InlineData("in 3 days",  3)]
    [InlineData("in 1 day",   1)]
    [InlineData("in 2 weeks", 14)]
    [InlineData("in 1 week",  7)]
    public void TryParseDate_ReturnsRelativeDate_ForInNUnitsFormat(string token, int expectedDaysFromNow)
    {
        var result = TaskDateParser.TryParseDate(token, out var value);

        Assert.True(result);
        Assert.Equal(DateTimeOffset.UtcNow.Date.AddDays(expectedDaysFromNow), value.Date);
    }

    [Fact]
    public void TryParseDate_ReturnsAbsoluteDate_ForIsoFormat()
    {
        var result = TaskDateParser.TryParseDate("2026-07-04", out var value);

        Assert.True(result);
        Assert.Equal(2026,         value.Year);
        Assert.Equal(7,            value.Month);
        Assert.Equal(4,            value.Day);
    }

    // NextDayOfWeek always skips to the NEXT week when today matches the target.
    // This is the documented behavior: "monday" when today IS Monday = next Monday.
    // The existing TryParseDate_ReturnsNextOccurrence_ForWeekdayToken only checks
    // DayOfWeek equality — it does NOT verify same-week vs next-week semantics.

    [Fact]
    public void NextDayOfWeek_ReturnsSevenDaysAhead_WhenFromDayMatchesTarget()
    {
        var monday = new DateTimeOffset(2026, 6, 8, 0, 0, 0, TimeSpan.Zero); // a known Monday

        var result = TaskDateParser.NextDayOfWeek(monday, DayOfWeek.Monday);

        Assert.Equal(monday.Date.AddDays(7), result.Date);
    }

    [Fact]
    public void NextDayOfWeek_ReturnsCorrectDaysAhead_WhenTargetIsLaterInWeek()
    {
        var monday = new DateTimeOffset(2026, 6, 8, 0, 0, 0, TimeSpan.Zero); // Monday

        var result = TaskDateParser.NextDayOfWeek(monday, DayOfWeek.Friday);

        Assert.Equal(monday.Date.AddDays(4), result.Date);
        Assert.Equal(DayOfWeek.Friday, result.DayOfWeek);
    }

    // ================================================================
    // TryExtractDueDateFromTitle — suffix detection
    // ================================================================

    [Fact]
    public void TryExtractDueDateFromTitle_ExtractsByPrefix_WhenTitleEndsWithByTomorrow()
    {
        var result = TaskDateParser.TryExtractDueDateFromTitle( "Fix the login bug by tomorrow"
                                                              , out var cleanTitle
                                                              , out var dueDate);

        Assert.True(result);
        Assert.Equal("Fix the login bug",                        cleanTitle);
        Assert.Equal(DateTimeOffset.UtcNow.Date.AddDays(1), dueDate!.Value.Date);
    }

    [Fact]
    public void TryExtractDueDateFromTitle_ExtractsByPrefix_WhenTitleEndsWithDueDate()
    {
        var result = TaskDateParser.TryExtractDueDateFromTitle( "Deploy release due 2026-07-04"
                                                              , out var cleanTitle
                                                              , out var dueDate);

        Assert.True(result);
        Assert.Equal("Deploy release",  cleanTitle);
        Assert.Equal(2026,              dueDate!.Value.Year);
        Assert.Equal(7,                 dueDate!.Value.Month);
        Assert.Equal(4,                 dueDate!.Value.Day);
    }

    [Fact]
    public void TryExtractDueDateFromTitle_ReturnsFalse_WhenNoSuffixPresent()
    {
        var result = TaskDateParser.TryExtractDueDateFromTitle( "Fix the login bug"
                                                              , out var cleanTitle
                                                              , out var dueDate);

        Assert.False(result);
        Assert.Equal("Fix the login bug", cleanTitle);
        Assert.Null(dueDate);
    }

    [Fact]
    public void TryExtractDueDateFromTitle_ReturnsFalse_WhenSuffixDateIsUnparseable()
    {
        var result = TaskDateParser.TryExtractDueDateFromTitle( "Fix the login bug by someday"
                                                              , out var cleanTitle
                                                              , out var dueDate);

        Assert.False(result);
        Assert.Equal("Fix the login bug by someday", cleanTitle);
        Assert.Null(dueDate);
    }

    [Fact]
    public void TryExtractDueDateFromTitle_UsesLastSuffix_WhenMultipleCandidatesPresent()
    {
        // "by end of week" should be parsed, not the word "by" earlier in the title
        var result = TaskDateParser.TryExtractDueDateFromTitle( "Update task tracker by end of week"
                                                              , out var cleanTitle
                                                              , out var dueDate);

        Assert.True(result);
        Assert.Equal("Update task tracker", cleanTitle);
        Assert.NotNull(dueDate);
        Assert.Equal(DayOfWeek.Saturday, dueDate!.Value.DayOfWeek);
    }

    [Fact]
    public void TryExtractDueDateFromTitle_Handles_UntilPrefix()
    {
        var result = TaskDateParser.TryExtractDueDateFromTitle( "Finish report until Friday"
                                                              , out var cleanTitle
                                                              , out var dueDate);
 
        Assert.True(result);
        Assert.Equal("Finish report",     cleanTitle);
        Assert.Equal(DayOfWeek.Friday,    dueDate!.Value.DayOfWeek);
    }

    [Fact]
    public void TryExtractDueDateFromTitle_Handles_ColonSuffix()
    {
        var result = TaskDateParser.TryExtractDueDateFromTitle( "Buy groceries due:today"
                                                              , out var cleanTitle
                                                              , out var dueDate);

        Assert.True(result);
        Assert.Equal("Buy groceries",                     cleanTitle);
        Assert.Equal(DateTimeOffset.UtcNow.Date,          dueDate!.Value.Date);
    }
}
