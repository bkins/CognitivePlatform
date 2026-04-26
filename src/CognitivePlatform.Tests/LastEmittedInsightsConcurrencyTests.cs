using CognitivePlatform.Api.Conversation;
using CognitivePlatform.Api.Insights.Models;

namespace CognitivePlatform.Tests;

// EPIC-06 Phase A: LastEmittedInsights uses the BUG-14 immutable-snapshot pattern.
// Readers must always observe a consistent reference even while a writer churns
// the field. Mirrors the shape of SetLlmSession_KeepsProviderAndModelConsistent_UnderContention.
public class LastEmittedInsightsConcurrencyTests
{
    [Fact]
    public void Default_LastEmittedInsights_Is_EmptyAndNotNull()
    {
        var context = new ConversationContext("session");

        Assert.NotNull(context.LastEmittedInsights);
        Assert.Empty(context.LastEmittedInsights);
    }

    [Fact]
    public void SetLastEmittedInsights_AcceptsNull_AndCoercesToEmpty()
    {
        var context = new ConversationContext("session");
        context.SetLastEmittedInsights(new[] { new EmittedInsightRef("k", null) });

        context.SetLastEmittedInsights(null);

        Assert.NotNull(context.LastEmittedInsights);
        Assert.Empty(context.LastEmittedInsights);
    }

    [Fact]
    public void Reset_ClearsLastEmittedInsights()
    {
        var context = new ConversationContext("session");
        context.SetLastEmittedInsights(new[] { new EmittedInsightRef("k", "Action") });

        context.Reset();

        Assert.Empty(context.LastEmittedInsights);
    }

    // Concurrency: a reader that captures LastEmittedInsights into a local must see
    // the same list reference for the entire scope of that local — never a half-swapped
    // intermediate. The snapshot pattern guarantees this because the field IS the list,
    // not a mutable container.
    [Fact]
    public async Task Reader_AlwaysObservesConsistentSnapshot_UnderContention()
    {
        var context = new ConversationContext("race-session");

        var snapshots = new (IReadOnlyList<EmittedInsightRef> list, string firstKey, int count)[]
                        {
                                (new[] { new EmittedInsightRef("alpha.k1", null) }, "alpha.k1", 1)
                              , (new[] { new EmittedInsightRef("beta.k1",  null)
                                       , new EmittedInsightRef("beta.k2",  "DoB") }, "beta.k1", 2)
                              , (Array.Empty<EmittedInsightRef>(), string.Empty, 0)
                        };

        context.SetLastEmittedInsights(snapshots[0].list);

        var inconsistent = 0;
        var iterations   = 10_000;
        var started      = new TaskCompletionSource();

        var reader = Task.Run(() =>
        {
            started.SetResult();

            for (var i = 0; i < iterations; i++)
            {
                var snapshot = context.LastEmittedInsights;

                // Compare against expected shapes — the reader should match exactly one
                // of the three snapshots above. A torn read would show e.g. count=2 with
                // alpha's first key, which is impossible if the swap is atomic.
                var matched = snapshots.Any(expected =>
                                  expected.count == snapshot.Count
                               && (snapshot.Count == 0 || snapshot[0].DeduplicationKey == expected.firstKey));

                if (matched.Equals(false))
                    Interlocked.Increment(ref inconsistent);
            }
        });

        await started.Task;

        var writer = Task.Run(() =>
        {
            for (var i = 0; i < iterations; i++)
            {
                var next = snapshots[i % snapshots.Length];
                context.SetLastEmittedInsights(next.list);
            }
        });

        await Task.WhenAll(writer, reader);

        Assert.Equal(0, inconsistent);
    }
}
