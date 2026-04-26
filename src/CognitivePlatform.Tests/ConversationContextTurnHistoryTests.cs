using CognitivePlatform.Api.Conversation;

namespace CognitivePlatform.Tests;

public class ConversationContextTurnHistoryTests
{
    private static readonly DateTimeOffset Anchor = new(2026, 4, 26, 12, 0, 0, TimeSpan.Zero);

    private static ConversationContext MakeContext(int maxHistory = 50, int maxLength = 4096) =>
        new("session", new ConversationContextOptions
                       {
                               MaxTurnHistory       = maxHistory
                             , MaxTurnMessageLength = maxLength
                       });

    private static ConversationTurn Turn(string userMessage      = "u"
                                       , string assistantMessage = "a"
                                       , TurnPath path           = TurnPath.Interpreter)
        => new(userMessage, assistantMessage, Anchor, path);

    [Fact]
    public void Default_Turns_IsEmptyAndNotNull()
    {
        var context = new ConversationContext("session");

        Assert.NotNull(context.Turns);
        Assert.Empty(context.Turns);
    }

    [Fact]
    public void RecordTurn_Appends_InOrder()
    {
        var context = MakeContext();

        context.RecordTurn(Turn(userMessage: "first"));
        context.RecordTurn(Turn(userMessage: "second"));
        context.RecordTurn(Turn(userMessage: "third"));

        Assert.Equal(3, context.Turns.Count);
        Assert.Equal("first",  context.Turns[0].UserMessage);
        Assert.Equal("second", context.Turns[1].UserMessage);
        Assert.Equal("third",  context.Turns[2].UserMessage);
    }

    [Fact]
    public void RecordTurn_Evicts_OldestEntry_WhenCapExceeded()
    {
        var context = MakeContext(maxHistory: 3);

        context.RecordTurn(Turn(userMessage: "one"));
        context.RecordTurn(Turn(userMessage: "two"));
        context.RecordTurn(Turn(userMessage: "three"));
        context.RecordTurn(Turn(userMessage: "four"));

        Assert.Equal(3, context.Turns.Count);
        Assert.Equal("two",   context.Turns[0].UserMessage);
        Assert.Equal("three", context.Turns[1].UserMessage);
        Assert.Equal("four",  context.Turns[2].UserMessage);
    }

    [Fact]
    public void RecordTurn_TruncatesUserMessage_AndAppendsMarker()
    {
        var context  = MakeContext(maxLength: 16);
        var oversize = new string('x', 100);

        context.RecordTurn(Turn(userMessage: oversize, assistantMessage: "ok"));

        var stored = context.Turns.Single();
        Assert.Equal(16 + ConversationContextOptions.TruncationMarker.Length
                   , stored.UserMessage.Length);
        Assert.EndsWith(ConversationContextOptions.TruncationMarker, stored.UserMessage);
        Assert.StartsWith(new string('x', 16), stored.UserMessage);
        Assert.Equal("ok", stored.AssistantMessage);
    }

    [Fact]
    public void RecordTurn_TruncatesAssistantMessage_AndAppendsMarker()
    {
        var context  = MakeContext(maxLength: 8);
        var oversize = new string('y', 100);

        context.RecordTurn(Turn(userMessage: "ok", assistantMessage: oversize));

        var stored = context.Turns.Single();
        Assert.Equal("ok", stored.UserMessage);
        Assert.Equal(8 + ConversationContextOptions.TruncationMarker.Length
                   , stored.AssistantMessage.Length);
        Assert.EndsWith(ConversationContextOptions.TruncationMarker, stored.AssistantMessage);
    }

    [Fact]
    public void RecordTurn_LeavesShortMessages_Unchanged()
    {
        var context = MakeContext(maxLength: 4096);

        context.RecordTurn(Turn(userMessage: "tiny", assistantMessage: "also tiny"));

        var stored = context.Turns.Single();
        Assert.Equal("tiny",      stored.UserMessage);
        Assert.Equal("also tiny", stored.AssistantMessage);
    }

    [Fact]
    public void Turns_Snapshot_IsImmutable_OnceRead()
    {
        var context = MakeContext();
        context.RecordTurn(Turn(userMessage: "first"));

        var snapshot = context.Turns;

        context.RecordTurn(Turn(userMessage: "second"));

        // The snapshot the reader took before the second write is unaffected.
        Assert.Single(snapshot);
        Assert.Equal("first", snapshot[0].UserMessage);

        // The current snapshot reflects the new write.
        Assert.Equal(2, context.Turns.Count);
    }

    [Fact]
    public void ReplaceLatestTurn_SwapsLastEntry_WithoutAffectingEarlierTurns()
    {
        var context = MakeContext();
        context.RecordTurn(Turn(userMessage: "first",  assistantMessage: "raw1"));
        context.RecordTurn(Turn(userMessage: "second", assistantMessage: "raw2"));

        var replacement = new ConversationTurn("second", "woven2", Anchor, TurnPath.Interpreter, "ActX", true);
        context.ReplaceLatestTurn(replacement);

        Assert.Equal(2, context.Turns.Count);
        Assert.Equal("raw1",   context.Turns[0].AssistantMessage);
        Assert.Equal("woven2", context.Turns[1].AssistantMessage);
        Assert.Equal("ActX",   context.Turns[1].ActionName);
        Assert.True(context.Turns[1].Succeeded);
    }

    [Fact]
    public void ReplaceLatestTurn_TruncatesMessages_OnReplacement()
    {
        var context = MakeContext(maxLength: 4);
        context.RecordTurn(Turn(userMessage: "ok", assistantMessage: "ok"));

        var oversize    = new string('z', 50);
        var replacement = new ConversationTurn("ok", oversize, Anchor, TurnPath.Interpreter);
        context.ReplaceLatestTurn(replacement);

        var stored = context.Turns.Single();
        Assert.Equal(4 + ConversationContextOptions.TruncationMarker.Length
                   , stored.AssistantMessage.Length);
    }

    [Fact]
    public void ReplaceLatestTurn_Throws_WhenHistoryIsEmpty()
    {
        var context = MakeContext();

        Assert.Throws<InvalidOperationException>(() =>
            context.ReplaceLatestTurn(Turn(userMessage: "doomed")));
    }

    [Fact]
    public void Reset_Clears_Turns()
    {
        var context = MakeContext();
        context.RecordTurn(Turn(userMessage: "first"));
        context.RecordTurn(Turn(userMessage: "second"));

        context.Reset();

        Assert.Empty(context.Turns);
    }

    [Fact]
    public void RecordTurn_NullArgument_Throws()
    {
        var context = MakeContext();

        Assert.Throws<ArgumentNullException>(() => context.RecordTurn(null!));
    }
}
