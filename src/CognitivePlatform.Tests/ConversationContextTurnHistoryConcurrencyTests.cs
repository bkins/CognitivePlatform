using CognitivePlatform.Api.Conversation;

namespace CognitivePlatform.Tests;

// ENH-08: Turns uses the BUG-14 immutable-snapshot pattern. The orchestrator is the
// only writer (single-writer contract), but readers (insight providers, future
// telemetry) may run concurrently with writes — they must always see a consistent
// IReadOnlyList reference, not a half-built collection.
public class ConversationContextTurnHistoryConcurrencyTests
{
    private static readonly DateTimeOffset Anchor = new(2026, 4, 26, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Reader_AlwaysObservesConsistentSnapshot_UnderContention()
    {
        var context = new ConversationContext("race-session"
                                            , new ConversationContextOptions { MaxTurnHistory = 50 });

        // Seed something so the reader has a non-empty starting state.
        context.RecordTurn(new ConversationTurn("seed", "ack", Anchor, TurnPath.FastPath));

        var inconsistent = 0;
        var iterations   = 10_000;
        var started      = new TaskCompletionSource();

        var reader = Task.Run(() =>
        {
            started.SetResult();

            for (var i = 0; i < iterations; i++)
            {
                var snapshot = context.Turns;

                // Walk the snapshot — every entry should be a fully-constructed
                // ConversationTurn (non-null) and the count should match the snapshot's
                // declared length all the way through. A torn read would surface as
                // either a null entry or a count that doesn't match the iteration.
                var declaredCount = snapshot.Count;
                var walked        = 0;
                foreach (var entry in snapshot)
                {
                    if (entry is null)
                    {
                        Interlocked.Increment(ref inconsistent);
                        break;
                    }
                    walked++;
                }

                if (walked != declaredCount)
                    Interlocked.Increment(ref inconsistent);
            }
        });

        await started.Task;

        var writer = Task.Run(() =>
        {
            for (var i = 0; i < iterations; i++)
            {
                var turn = new ConversationTurn(
                                   UserMessage:      $"u{i}"
                                 , AssistantMessage: $"a{i}"
                                 , OccurredAt:       Anchor
                                 , Path:             TurnPath.Interpreter);
                context.RecordTurn(turn);
            }
        });

        await Task.WhenAll(reader, writer);

        Assert.Equal(0, inconsistent);
    }

    [Fact]
    public async Task Reader_AlwaysObservesConsistentSnapshot_AcrossReplaceLatestTurn()
    {
        var context = new ConversationContext("replace-race"
                                            , new ConversationContextOptions { MaxTurnHistory = 50 });

        context.RecordTurn(new ConversationTurn("seed", "raw", Anchor, TurnPath.Interpreter));

        var inconsistent = 0;
        var iterations   = 5_000;
        var started      = new TaskCompletionSource();

        var reader = Task.Run(() =>
        {
            started.SetResult();

            for (var i = 0; i < iterations; i++)
            {
                var snapshot = context.Turns;

                // The reader should never see an empty snapshot — RecordTurn ran once
                // before reads started and ReplaceLatestTurn never empties the list.
                if (snapshot.Count == 0)
                {
                    Interlocked.Increment(ref inconsistent);
                    continue;
                }

                var last = snapshot[^1];
                if (last is null)
                    Interlocked.Increment(ref inconsistent);
            }
        });

        await started.Task;

        var writer = Task.Run(() =>
        {
            for (var i = 0; i < iterations; i++)
            {
                var replacement = new ConversationTurn(
                                          UserMessage:      "seed"
                                        , AssistantMessage: $"woven-{i}"
                                        , OccurredAt:       Anchor
                                        , Path:             TurnPath.Interpreter);
                context.ReplaceLatestTurn(replacement);
            }
        });

        await Task.WhenAll(reader, writer);

        Assert.Equal(0, inconsistent);
    }
}
