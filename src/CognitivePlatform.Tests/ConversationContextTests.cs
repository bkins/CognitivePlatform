using CognitivePlatform.Api.Conversation;

namespace CognitivePlatform.Tests;

public class ConversationContextTests
{
    [Fact]
    public void SetLlmSession_PopulatesSnapshot()
    {
        var context = new ConversationContext("session");

        context.SetLlmSession("OpenRouter", "anthropic/claude-3.5-sonnet");

        var session = context.CurrentLlmSession;

        Assert.Equal("OpenRouter",                  session.Provider);
        Assert.Equal("anthropic/claude-3.5-sonnet", session.Model);
    }

    [Fact]
    public void SetLlmSession_OverwritesPreviousValues()
    {
        var context = new ConversationContext("session");
        context.SetLlmSession("Groq", "llama-3.3-70b-versatile");

        context.SetLlmSession("Gemini", "gemini-2.5-flash");

        var session = context.CurrentLlmSession;

        Assert.Equal("Gemini",           session.Provider);
        Assert.Equal("gemini-2.5-flash", session.Model);
    }

    [Fact]
    public void SetLlmModel_UpdatesModel_WhilePreservingProvider()
    {
        var context = new ConversationContext("session");
        context.SetLlmSession("OpenRouter", "anthropic/claude-3.5-sonnet");

        context.SetLlmModel("anthropic/claude-3.7-haiku");

        var session = context.CurrentLlmSession;

        Assert.Equal("OpenRouter",                  session.Provider);
        Assert.Equal("anthropic/claude-3.7-haiku",  session.Model);
    }

    [Fact]
    public void Reset_ClearsLlmSession_ToEmpty()
    {
        var context = new ConversationContext("session");
        context.SetLlmSession("Gemini", "gemini-2.5-flash");

        context.Reset();

        Assert.Same(LlmSession.Empty, context.CurrentLlmSession);
        Assert.False(context.CurrentLlmSession.HasProvider);
        Assert.False(context.CurrentLlmSession.HasModel);
    }

    [Fact]
    public void Metadata_SupportsCaseInsensitiveReads_AfterSwitchToConcurrentDictionary()
    {
        var context = new ConversationContext("session");
        context.Metadata["KeY"] = "value";

        Assert.True(context.Metadata.TryGetValue("key", out var fetched));
        Assert.Equal("value", fetched);
    }

    // BUG-14: With the immutable-snapshot model, a reader cannot observe a torn
    // (provider, model) pair regardless of how aggressively a writer mutates the
    // session. Deterministic; runs in both the full suite and in isolation.
    [Fact]
    public async Task SetLlmSession_KeepsProviderAndModelConsistent_UnderContention()
    {
        var context = new ConversationContext("race-session");

        var pairs = new (string provider, string model)[]
                    {
                            ("Groq",       "llama-3.3-70b-versatile")
                          , ("OpenRouter", "anthropic/claude-3.5-sonnet")
                          , ("Gemini",     "gemini-2.5-flash")
                    };

        context.SetLlmSession(pairs[0].provider, pairs[0].model);

        var torn       = 0;
        var readsOk    = 0;
        var iterations = 10_000;
        var started    = new TaskCompletionSource();

        var reader = Task.Run(() =>
        {
            started.SetResult();

            for (var i = 0; i < iterations; i++)
            {
                var session = context.CurrentLlmSession;

                if (session.HasProvider.Equals(false) || session.HasModel.Equals(false))
                    continue;

                var expected = pairs.FirstOrDefault(pair => pair.provider == session.Provider);

                if (expected.model is not null && expected.model == session.Model)
                    Interlocked.Increment(ref readsOk);
                else
                    Interlocked.Increment(ref torn);
            }
        });

        await started.Task;

        var writer = Task.Run(() =>
        {
            for (var i = 0; i < iterations; i++)
            {
                var next = pairs[i % pairs.Length];
                context.SetLlmSession(next.provider, next.model);
            }
        });

        await Task.WhenAll(writer, reader);

        Assert.Equal(0, torn);
        Assert.True(readsOk > 0, "Reader should have observed at least one consistent pair.");
    }

    // BUG-14: Reset() must atomically swap the snapshot back to Empty. A reader
    // racing Reset() should see either the previous (provider, model) pair or
    // the empty pair — never a half-cleared mix.
    [Fact]
    public async Task Reset_KeepsSessionSnapshotConsistent_UnderContention()
    {
        var context = new ConversationContext("reset-race");
        var pair    = ("OpenRouter", "anthropic/claude-3.5-sonnet");

        context.SetLlmSession(pair.Item1, pair.Item2);

        var torn       = 0;
        var iterations = 10_000;
        var started    = new TaskCompletionSource();

        var reader = Task.Run(() =>
        {
            started.SetResult();

            for (var i = 0; i < iterations; i++)
            {
                var session = context.CurrentLlmSession;

                var isPair  = session.Provider == pair.Item1 && session.Model == pair.Item2;
                var isEmpty = session.HasProvider.Equals(false) && session.HasModel.Equals(false);

                if (isPair.Equals(false) && isEmpty.Equals(false))
                    Interlocked.Increment(ref torn);
            }
        });

        await started.Task;

        var writer = Task.Run(() =>
        {
            for (var i = 0; i < iterations; i++)
            {
                if ((i & 1) == 0)
                    context.SetLlmSession(pair.Item1, pair.Item2);
                else
                    context.Reset();
            }
        });

        await Task.WhenAll(writer, reader);

        Assert.Equal(0, torn);
    }
}
