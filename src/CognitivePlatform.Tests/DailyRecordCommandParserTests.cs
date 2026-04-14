using CognitivePlatform.Api.Domains.DailyRecord;

namespace CognitivePlatform.Tests;

public class DailyRecordCommandParserTests
{
    private readonly DailyRecordCommandParser _parser = new();

    // ================================================================
    // PREFIX DETECTION — CommandType
    // ================================================================

    [Fact]
    public void Parse_Returns_Plan_CommandType_ForPlanPrefix()
    {
        var result = _parser.Parse("Plan: Focus on the refactor today.");

        Assert.Equal(DailyCommandType.Plan, result.CommandType);
    }

    [Fact]
    public void Parse_Returns_Check_CommandType_ForCheckPrefix()
    {
        var result = _parser.Parse("Check: Finished the streaming handler.");

        Assert.Equal(DailyCommandType.Check, result.CommandType);
    }

    [Theory]
    [InlineData("EOD: Good day overall.")]
    [InlineData("Done: Wrapping up for the night.")]
    [InlineData("Evening: Solid progress today.")]
    [InlineData("DayDone: All done.")]
    public void Parse_Returns_EndOfDay_CommandType_ForAllEodAliases(string input)
    {
        var result = _parser.Parse(input);

        Assert.Equal(DailyCommandType.EndOfDay, result.CommandType);
    }

    [Fact]
    public void Parse_Returns_Unknown_WhenPrefixNotRecognised()
    {
        var result = _parser.Parse("Journal: This is a journal entry.");

        Assert.Equal(DailyCommandType.Unknown, result.CommandType);
    }

    [Fact]
    public void Parse_Returns_Unknown_WhenInputIsEmpty()
    {
        var result = _parser.Parse(string.Empty);

        Assert.Equal(DailyCommandType.Unknown, result.CommandType);
    }

    // ================================================================
    // BODY TEXT EXTRACTION
    // ================================================================

    [Fact]
    public void Parse_ExtractsBodyText_FromFirstLine()
    {
        var result = _parser.Parse("Plan: Focus on the refactor today.");

        Assert.Equal("Focus on the refactor today.", result.BodyText);
    }

    [Fact]
    public void Parse_ConcatenatesBodyText_AcrossMultipleLines()
    {
        var input = """
                    Plan: Focus on the refactor today.
                    Also need to review some PRs.
                    """;

        var result = _parser.Parse(input);

        Assert.Equal("Focus on the refactor today. Also need to review some PRs.", result.BodyText);
    }

    [Fact]
    public void Parse_BodyText_IsEmpty_WhenNothingFollowsPrefix()
    {
        var result = _parser.Parse("Plan:");

        Assert.Equal(string.Empty, result.BodyText);
    }

    // ================================================================
    // TASKS: BLOCK — multi-line bullets
    // ================================================================

    [Fact]
    public void Parse_ExtractsTasks_FromMultiLineBulletList()
    {
        var input = """
                    Plan: Deep work day.
                    Tasks:
                      - Finish the streaming handler
                      - Review PR from Sarah
                      - Write unit tests
                    """;

        var result = _parser.Parse(input);

        Assert.Equal(3,                               result.Tasks.Count);
        Assert.Equal("Finish the streaming handler",  result.Tasks[0]);
        Assert.Equal("Review PR from Sarah",          result.Tasks[1]);
        Assert.Equal("Write unit tests",              result.Tasks[2]);
    }

    [Fact]
    public void Parse_ExtractsTasks_FromInlineCommaSeparatedList()
    {
        var result = _parser.Parse("Plan: Busy day.\nTasks: Fix bug, Write test, Deploy");

        Assert.Equal(3,             result.Tasks.Count);
        Assert.Equal("Fix bug",     result.Tasks[0]);
        Assert.Equal("Write test",  result.Tasks[1]);
        Assert.Equal("Deploy",      result.Tasks[2]);
    }

    [Fact]
    public void Parse_ExtractsTasks_WhenInlineOnFirstLine()
    {
        var input = "Plan: Focused day. Tasks: Fix the rollover bug, Write the test plan doc, Review PR";

        var result = _parser.Parse(input);

        Assert.Equal(DailyCommandType.Plan,        result.CommandType);
        Assert.Equal("Focused day.",               result.BodyText);
        Assert.Equal(3,                            result.Tasks.Count);
        Assert.Contains("Fix the rollover bug",    result.Tasks);
        Assert.Contains("Write the test plan doc", result.Tasks);
        Assert.Contains("Review PR",               result.Tasks);
    }

    [Fact]
    public void Parse_ExtractsMoodAndTasks_WhenAllInlineOnFirstLine()
    {
        var input = "Plan: Good morning. Tasks: Respond to emails, Finish report Mood: Calm MoodScore: 4";

        var result = _parser.Parse(input);

        Assert.Equal("Good morning.",         result.BodyText);
        Assert.Equal(2,                       result.Tasks.Count);
        Assert.Contains("Respond to emails",  result.Tasks);
        Assert.Contains("Finish report",      result.Tasks);
        Assert.Equal("Calm",                  result.Mood);
        Assert.Equal(4,                       result.MoodScore);
    }

    [Fact]
    public void Parse_ExtractsTasks_WhenCarriageReturnLineEndings()
    {
        // MAUI Editor on Windows inserts \r rather than \n or \r\n.
        var input = "Plan: Focused day.\rTasks:\r- Fix the rollover bug\r- Write the test plan doc\r- Review PR";

        var result = _parser.Parse(input);

        Assert.Equal(DailyCommandType.Plan,        result.CommandType);
        Assert.Equal(3,                            result.Tasks.Count);
        Assert.Contains("Fix the rollover bug",    result.Tasks);
        Assert.Contains("Write the test plan doc", result.Tasks);
        Assert.Contains("Review PR",               result.Tasks);
    }

    [Fact]
    public void Parse_ExtractsTasks_WhenBlankLinesBetweenBullets()
    {
        var input = "Plan: Focused day.\n\nTasks:\n\n- Fix the rollover bug\n\n- Write the test plan doc\n\n- Review PR";

        var result = _parser.Parse(input);

        Assert.Equal(3,                        result.Tasks.Count);
        Assert.Contains("Fix the rollover bug",    result.Tasks);
        Assert.Contains("Write the test plan doc", result.Tasks);
        Assert.Contains("Review PR",               result.Tasks);
    }

    [Fact]
    public void Parse_Returns_EmptyTasks_WhenNoTasksBlock()
    {
        var result = _parser.Parse("Plan: Quiet day.");

        Assert.Empty(result.Tasks);
    }

    [Fact]
    public void Parse_BodyText_DoesNotIncludeTaskLines()
    {
        var input = """
                    Plan: Deep work day.
                    Tasks:
                      - Finish the handler
                    """;

        var result = _parser.Parse(input);

        Assert.Equal("Deep work day.", result.BodyText);
        Assert.DoesNotContain("Finish", result.BodyText);
    }

    // ================================================================
    // TAGS: / MOOD: / MOODSCORE: DIRECTIVES
    // ================================================================

    [Fact]
    public void Parse_ExtractsTags_WhenQuoted()
    {
        var input = """
                    Plan: Deep work day.
                    Tags: "work", "focus"
                    """;

        var result = _parser.Parse(input);

        Assert.Equal(new[] { "work", "focus" }, result.Tags);
    }

    [Fact]
    public void Parse_ExtractsMood_WhenQuoted()
    {
        var input = """
                    Plan: Productive morning.
                    Mood: "Determined"
                    """;

        var result = _parser.Parse(input);

        Assert.Equal("Determined", result.Mood);
    }

    [Fact]
    public void Parse_ExtractsMoodScore_WhenValid()
    {
        var input = """
                    Plan: Good start.
                    MoodScore: 4
                    """;

        var result = _parser.Parse(input);

        Assert.Equal(4, result.MoodScore);
    }

    [Fact]
    public void Parse_Returns_NullMoodScore_WhenOutOfRange()
    {
        var input = """
                    Plan: Hectic day.
                    MoodScore: 9
                    """;

        var result = _parser.Parse(input);

        Assert.Null(result.MoodScore);
    }

    [Fact]
    public void Parse_Returns_NullMoodScore_WhenNonNumeric()
    {
        var input = """
                    Plan: Good morning.
                    MoodScore: great
                    """;

        var result = _parser.Parse(input);

        Assert.Null(result.MoodScore);
    }

    [Fact]
    public void Parse_Extracts_AllDirectives_Together()
    {
        var input = """
                    Plan: Focused morning.
                    Tags: "work", "deep-focus"
                    Tasks:
                      - Write unit tests
                      - Review architecture doc
                    Mood: "Energised"
                    MoodScore: 5
                    """;

        var result = _parser.Parse(input);

        Assert.Equal(DailyCommandType.Plan,           result.CommandType);
        Assert.Equal("Focused morning.",              result.BodyText);
        Assert.Equal(new[] { "work", "deep-focus" },  result.Tags);
        Assert.Equal(2,                               result.Tasks.Count);
        Assert.Equal("Energised",                     result.Mood);
        Assert.Equal(5,                               result.MoodScore);
    }

    // ================================================================
    // EOD — no tasks expected, closing text only
    // ================================================================

    [Fact]
    public void Parse_ExtractsClosingText_ForEodCommand()
    {
        var input = """
                    EOD: Good day overall. Didn't finish the docs task.
                    Mood: "Satisfied"
                    MoodScore: 4
                    """;

        var result = _parser.Parse(input);

        Assert.Equal(DailyCommandType.EndOfDay,               result.CommandType);
        Assert.Equal("Good day overall. Didn't finish the docs task.", result.BodyText);
        Assert.Equal("Satisfied",                             result.Mood);
        Assert.Equal(4,                                       result.MoodScore);
        Assert.Empty(result.Tasks);
    }

    // ================================================================
    // StartsWithKnownPrefix
    // ================================================================

    [Theory]
    [InlineData("Plan: Today I'm focusing on the API.")]
    [InlineData("Check: Good progress so far.")]
    [InlineData("EOD: Wrapping up.")]
    [InlineData("Done: All done for today.")]
    [InlineData("Evening: Reflective evening.")]
    [InlineData("DayDone: Closing out.")]
    public void StartsWithKnownPrefix_ReturnsTrue_ForKnownPrefixes(string input)
    {
        Assert.True(DailyRecordCommandParser.StartsWithKnownPrefix(input));
    }

    [Theory]
    [InlineData("Journal: This is a journal entry.")]
    [InlineData("Task: Buy milk")]
    [InlineData("Show my tasks")]
    [InlineData("")]
    public void StartsWithKnownPrefix_ReturnsFalse_ForUnknownInput(string input)
    {
        Assert.False(DailyRecordCommandParser.StartsWithKnownPrefix(input));
    }
}
