using CognitivePlatform.Api.Domains.Journal;

namespace CognitivePlatform.Tests;

public class JournalCommandParserTests
{
    private readonly IJournalCommandParser _parser =
            new JournalCommandParser();

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
    public void Ignores_Unquoted_Tags()
    {
        var input = """
                    Just writing.
                    Tags: work, planning
                    """;

        var result = _parser.Parse(input);

        Assert.Equal(input, result.Text); // Just leave the input as-is.  Later allow for unquoted directives
        Assert.Empty(result.Tags);
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
}
