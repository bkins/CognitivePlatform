using CognitivePlatform.Api.Conversation;

namespace CognitivePlatform.Tests;

public class ConversationTurnTests
{
    private static readonly DateTimeOffset Anchor = new(2026, 4, 26, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Construction_ExposesAllPositionalFields()
    {
        var turn = new ConversationTurn(
                       UserMessage:      "hi"
                     , AssistantMessage: "hello"
                     , OccurredAt:       Anchor
                     , Path:             TurnPath.Interpreter
                     , ActionName:       "GetTime"
                     , Succeeded:        true);

        Assert.Equal("hi",                  turn.UserMessage);
        Assert.Equal("hello",               turn.AssistantMessage);
        Assert.Equal(Anchor,                turn.OccurredAt);
        Assert.Equal(TurnPath.Interpreter,  turn.Path);
        Assert.Equal("GetTime",             turn.ActionName);
        Assert.True(turn.Succeeded);
    }

    [Fact]
    public void OptionalFields_DefaultToNull()
    {
        var turn = new ConversationTurn("u", "a", Anchor, TurnPath.FastPath);

        Assert.Null(turn.ActionName);
        Assert.Null(turn.Succeeded);
    }

    [Fact]
    public void RecordEquality_TreatsTwoTurnsWithSameValues_AsEqual()
    {
        var first  = new ConversationTurn("u", "a", Anchor, TurnPath.Confirmation, "Del", false);
        var second = new ConversationTurn("u", "a", Anchor, TurnPath.Confirmation, "Del", false);

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void RecordEquality_DistinguishesByPath()
    {
        var fastPath    = new ConversationTurn("u", "a", Anchor, TurnPath.FastPath);
        var interpreter = new ConversationTurn("u", "a", Anchor, TurnPath.Interpreter);

        Assert.NotEqual(fastPath, interpreter);
    }

    [Fact]
    public void WithExpression_ProducesNewInstance_LeavingOriginalUntouched()
    {
        var original = new ConversationTurn("u", "raw", Anchor, TurnPath.Interpreter, "X", true);

        var rewritten = original with { AssistantMessage = "woven" };

        Assert.Equal("raw",   original.AssistantMessage);
        Assert.Equal("woven", rewritten.AssistantMessage);
        Assert.Equal(original.UserMessage, rewritten.UserMessage);
        Assert.Equal(original.OccurredAt,  rewritten.OccurredAt);
        Assert.Equal(original.Path,        rewritten.Path);
        Assert.Equal(original.ActionName,  rewritten.ActionName);
        Assert.Equal(original.Succeeded,   rewritten.Succeeded);
    }
}
