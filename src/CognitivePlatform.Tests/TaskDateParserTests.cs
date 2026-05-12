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

    [Fact]
    public void TryParseDate_ReturnsRelativeDate_ForInNDaysFormat()
    {
        var result = TaskDateParser.TryParseDate("in 3 days", out var value);

        Assert.True(result);
        Assert.Equal(DateTimeOffset.UtcNow.Date.AddDays(3), value.Date);
    }

    [Fact]
    public void TryParseDate_ReturnsRelativeDate_ForInNWeeksFormat()
    {
        var result = TaskDateParser.TryParseDate("in 2 weeks", out var value);

        Assert.True(result);
        Assert.Equal(DateTimeOffset.UtcNow.Date.AddDays(14), value.Date);
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
}
