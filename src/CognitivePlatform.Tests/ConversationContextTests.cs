using CognitivePlatform.Api.Actions;
using CognitivePlatform.Api.Conversation;

namespace CognitivePlatform.Tests;

public class ConversationContextTests
{
    [Fact]
    public void SetLlmSession_WritesBothKeys()
    {
        var context = new ConversationContext("session");

        context.SetLlmSession("OpenRouter", "anthropic/claude-3.5-sonnet");

        Assert.Equal("OpenRouter",                   context.Metadata[LlmActions.SessionProviderKey]);
        Assert.Equal("anthropic/claude-3.5-sonnet", context.Metadata[LlmActions.SessionModelKey]);
    }

    [Fact]
    public void SetLlmSession_OverwritesPreviousValues()
    {
        var context = new ConversationContext("session");
        context.SetLlmSession("Groq", "llama-3.3-70b-versatile");

        context.SetLlmSession("Gemini", "gemini-2.0-flash");

        Assert.Equal("Gemini",           context.Metadata[LlmActions.SessionProviderKey]);
        Assert.Equal("gemini-2.0-flash", context.Metadata[LlmActions.SessionModelKey]);
    }

    [Fact]
    public void Metadata_SupportsCaseInsensitiveReads_AfterSwitchToConcurrentDictionary()
    {
        var context = new ConversationContext("session");
        context.Metadata["KeY"] = "value";

        Assert.True(context.Metadata.TryGetValue("key", out var fetched));
        Assert.Equal("value", fetched);
    }

    [Fact]
    public async Task SetLlmSession_KeepsProviderAndModelConsistent_UnderContention()
    {
        var context = new ConversationContext("race-session");

        // Pairs the writer thread alternates between. If the write were not atomic,
        // a reader could observe provider=A with model belonging to B.
        var pairs = new (string provider, string model)[]
                    {
                            ("Groq",       "llama-3.3-70b-versatile")
                          , ("OpenRouter", "anthropic/claude-3.5-sonnet")
                          , ("Gemini",     "gemini-2.0-flash")
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
                if (context.Metadata.TryGetValue(LlmActions.SessionProviderKey, out var provider).Equals(false))
                    continue;
                if (context.Metadata.TryGetValue(LlmActions.SessionModelKey, out var model).Equals(false))
                    continue;

                var expected = pairs.FirstOrDefault(pair => pair.provider == provider);

                if (expected.model is not null && expected.model == model)
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
}
