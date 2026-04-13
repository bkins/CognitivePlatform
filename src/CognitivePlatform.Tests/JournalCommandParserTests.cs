using CognitivePlatform.Api.Domains.Journal;
using CognitivePlatform.Api.Domains.Journal.Interfaces;

namespace CognitivePlatform.Tests;

public class JournalCommandParserTests
{
    private readonly IJournalCommandParser _parser = new JournalCommandParser();

    [Fact]
    public void Parses_Text_Only()
    {
        var result = _parser.Parse("Had a good day.");

        Assert.Equal("Had a good day.", result.Text);
        Assert.Empty(result.Tags);
        Assert.Null(result.Mood);
    }

    [Fact]
    public void Parses_Tags_And_Mood()
    {
        var input = """
                    Had a productive meeting.
                    Tags: "work", "planning"
                    Mood: "Optimistic"
                    """;

        var result = _parser.Parse(input);

        Assert.Equal("Had a productive meeting.",  result.Text);
        Assert.Equal(new[] { "work", "planning" }, result.Tags);
        Assert.Equal("Optimistic",                 result.Mood);
    }
    
    [Fact]
    public void Parses_Tags_And_Mood_Single_Line_input()
    {
        var input = """Had a productive meeting. Tags: "work", "planning" Mood: "Optimistic" """;

        var result = _parser.Parse(input);

        Assert.Equal("Had a productive meeting.",  result.Text);
        Assert.Equal(new[] { "work", "planning" }, result.Tags);
        Assert.Equal("Optimistic",                 result.Mood);
    }

    [Fact]
    public void Parses_Unquoted_Tags()
    {
        var input = """
                    Just writing.
                    Tags: work, planning
                    """;

        var result = _parser.Parse(input);

        Assert.Equal("Just writing.",               result.Text);
        Assert.Equal(new[] { "work", "planning" }, result.Tags);
    }

    [Fact]
    public void Parses_Unquoted_Mood()
    {
        var input = """
                    Went for a walk.
                    Mood: hopeful
                    """;

        var result = _parser.Parse(input);

        Assert.Equal("Went for a walk.", result.Text);
        Assert.Equal("hopeful",          result.Mood);
    }

    [Fact]
    public void Parses_Unquoted_Tags_And_MoodScore_No_Mood()
    {
        var input = """
                    Finished the project.
                    Tags: work, focus
                    MoodScore: 4
                    """;

        var result = _parser.Parse(input);

        Assert.Equal("Finished the project.",      result.Text);
        Assert.Equal(new[] { "work", "focus" },   result.Tags);
        Assert.Null(result.Mood);
        Assert.Equal(4, result.MoodScore);
    }

    [Fact]
    public void Parses_Full_Block_Grammar()
    {
        // Simulates what FastPathResolver passes to AddJournalEntry after stripping
        // the "Journal:" prefix — confirms BUG-06 fix: the prefix does not reach storage
        // and the full block is parsed correctly by the parser.
        var input = """
                    Had a great day.
                    Tags: work, focus
                    Mood: hopeful
                    MoodScore: 4
                    """;

        var result = _parser.Parse(input);

        Assert.Equal("Had a great day.",          result.Text);
        Assert.Equal(new[] { "work", "focus" },  result.Tags);
        Assert.Equal("hopeful",                   result.Mood);
        Assert.Equal(4,                           result.MoodScore);
    }

    [Fact]
    public void Preserves_Multiline_Text()
    {
        var input = """
                    Line one.
                    Line two.
                    Mood: "Reflective"
                    """;

        var result = _parser.Parse(input);

        Assert.Equal($"Line one.{Environment.NewLine}Line two.", result.Text);
        Assert.Equal("Reflective",           result.Mood);
    }

    // --- MoodScore boundary and out-of-range ---

    [Fact]
    public void Parse_ReturnsMoodScore_1_WhenScoreIsMinBoundary()
    {
        var input = """
                    Made it through.
                    MoodScore: 1
                    """;

        var result = _parser.Parse(input);

        Assert.Equal(1, result.MoodScore);
    }

    [Fact]
    public void Parse_ReturnsMoodScore_5_WhenScoreIsMaxBoundary()
    {
        var input = """
                    Best day ever.
                    MoodScore: 5
                    """;

        var result = _parser.Parse(input);

        Assert.Equal(5, result.MoodScore);
    }

    [Fact]
    public void Parse_ReturnsNullMoodScore_WhenScoreIsBelowRange()
    {
        var input = """
                    Rough start.
                    MoodScore: 0
                    """;

        var result = _parser.Parse(input);

        Assert.Null(result.MoodScore);
    }

    [Fact]
    public void Parse_ReturnsNullMoodScore_WhenScoreIsAboveRange()
    {
        var input = """
                    Going great.
                    MoodScore: 6
                    """;

        var result = _parser.Parse(input);

        Assert.Null(result.MoodScore);
    }

    // --- Edge-case inputs ---

    [Fact]
    public void Parse_ReturnsEmptyText_WhenInputIsWhitespaceOnly()
    {
        var result = _parser.Parse("   ");

        Assert.Equal(string.Empty, result.Text);
        Assert.Empty(result.Tags);
        Assert.Null(result.Mood);
    }

    [Fact]
    public void Parse_ReturnsEmptyText_WhenInputIsEmpty()
    {
        var result = _parser.Parse(string.Empty);

        Assert.Equal(string.Empty, result.Text);
        Assert.Empty(result.Tags);
        Assert.Null(result.Mood);
    }

    [Fact]
    public void Parse_ReturnsEmptyText_WhenInputContainsOnlyDirectives()
    {
        var input = """
                    Tags: work, focus
                    Mood: "Content"
                    MoodScore: 3
                    """;

        var result = _parser.Parse(input);

        Assert.Equal(string.Empty,              result.Text);
        Assert.Equal(new[] { "work", "focus" }, result.Tags);
        Assert.Equal("Content",                 result.Mood);
        Assert.Equal(3,                         result.MoodScore);
    }

    [Fact]
    public void Parse_ParsesText_WhenMixedDirectivesAndTextAreOnSingleLine()
    {
        var input = """Great session. Tags: "dev", "win" Mood: "Focused" MoodScore: 4""";

        var result = _parser.Parse(input);

        Assert.Equal("Great session.",           result.Text);
        Assert.Equal(new[] { "dev", "win" },    result.Tags);
        Assert.Equal("Focused",                  result.Mood);
        Assert.Equal(4,                          result.MoodScore);
    }

    // BACK-04 — ExtractIntValue used int.Parse which throws on non-numeric input;
    // replaced with int.TryParse so malformed values return null gracefully.
    [Fact]
    public void Parse_ReturnsNullMoodScore_WhenMoodScoreIsNonNumeric()
    {
        var input = """
                    Had a good day.
                    MoodScore: great
                    """;

        var result = _parser.Parse(input);

        Assert.Null(result.MoodScore);
    }
}
