using CognitivePlatform.Api.Actions;
using CognitivePlatform.Api.Avails;
using CognitivePlatform.Api.Avails.Models;
using CognitivePlatform.Api.Conversation;
using CognitivePlatform.Api.Interpreter;
using Microsoft.Extensions.Options;

namespace CognitivePlatform.Tests;

[Collection("LlmSharedState")]
public class LlmActionsTests
{
    private static ConversationContext MakeContext() =>
        new("test-session");

    private static LlmModelCatalog CatalogWith(params LlmModelInfo[] models)
    {
        var catalog = new LlmModelCatalog();
        foreach (var model in models)
            catalog.Add(model);
        return catalog;
    }

    private static LlmModelInfo Usable(string name) =>
        new(name, IsUsable: true, FailureReason: null, SupportsChat: true, SupportsStreaming: true);

    private static LlmModelInfo Unusable(string name) =>
        new(name, IsUsable: false, FailureReason: "probe failed", SupportsChat: false, SupportsStreaming: false);

    // ----------------------------------------------------------------
    // SetModel
    // ----------------------------------------------------------------

    [Fact]
    public void SetModel_StoresInSessionSnapshot_WhenModelIsUsable()
    {
        var context = MakeContext();
        var catalog = CatalogWith(Usable("llama3.1-8b"));

        LlmActions.SetContext(context);
        LlmActions.SetCatalog(catalog);

        var result = LlmActions.SetModel("llama3.1-8b");

        Assert.Contains("llama3.1-8b", result);
        Assert.Equal("llama3.1-8b", context.CurrentLlmSession.Model);
    }

    [Fact]
    public void SetModel_IsCaseInsensitive_WhenMatchingCatalog()
    {
        var context = MakeContext();
        var catalog = CatalogWith(Usable("LLaMa3.1-8B"));

        LlmActions.SetContext(context);
        LlmActions.SetCatalog(catalog);

        var result = LlmActions.SetModel("llama3.1-8b");

        Assert.Contains("LLaMa3.1-8B", result);
        Assert.Equal("LLaMa3.1-8B", context.CurrentLlmSession.Model);
    }

    [Fact]
    public void SetModel_RejectsUnknownModel_AndListsAvailable()
    {
        var context = MakeContext();
        var catalog = CatalogWith(Usable("llama3.1-8b"), Usable("gpt-4o-mini"));

        LlmActions.SetContext(context);
        LlmActions.SetCatalog(catalog);

        var result = LlmActions.SetModel("nonexistent-model");

        Assert.Contains("Unknown model", result);
        Assert.Contains("llama3.1-8b",   result);
        Assert.Contains("gpt-4o-mini",   result);
        Assert.False(context.CurrentLlmSession.HasModel);
    }

    [Fact]
    public void SetModel_RejectsUnusableModel_WithReason()
    {
        var context = MakeContext();
        var catalog = CatalogWith(Unusable("broken-model"));

        LlmActions.SetContext(context);
        LlmActions.SetCatalog(catalog);

        var result = LlmActions.SetModel("broken-model");

        Assert.Contains("not usable", result);
        Assert.False(context.CurrentLlmSession.HasModel);
    }

    [Fact]
    public void SetModel_ReturnsError_WhenModelIsEmpty()
    {
        var context = MakeContext();
        var catalog = CatalogWith(Usable("any-model"));

        LlmActions.SetContext(context);
        LlmActions.SetCatalog(catalog);

        var result = LlmActions.SetModel(string.Empty);

        Assert.Contains("provide a model name", result);
    }

    // ----------------------------------------------------------------
    // ListModels
    // ----------------------------------------------------------------

    [Fact]
    public void ListModels_ReturnsUsableModels()
    {
        var context = MakeContext();
        var catalog = CatalogWith(Usable("model-a"), Usable("model-b"), Unusable("broken"));

        LlmActions.SetContext(context);
        LlmActions.SetCatalog(catalog);

        var result = LlmActions.ListModels();

        Assert.Contains("model-a", result);
        Assert.Contains("model-b", result);
        Assert.Contains("broken",  result);
        Assert.Contains("unavailable", result);
    }

    [Fact]
    public void ListModels_MarksCurrentModel()
    {
        var context = MakeContext();
        context.SetLlmModel("model-a");
        var catalog = CatalogWith(Usable("model-a"), Usable("model-b"));

        LlmActions.SetContext(context);
        LlmActions.SetCatalog(catalog);

        var result = LlmActions.ListModels();

        Assert.Contains("model-a (current)", result);
    }

    [Fact]
    public void ListModels_ReturnsEmptyMessage_WhenCatalogIsEmpty()
    {
        var context = MakeContext();
        var catalog = new LlmModelCatalog();

        LlmActions.SetContext(context);
        LlmActions.SetCatalog(catalog);

        var result = LlmActions.ListModels();

        Assert.Contains("empty", result);
    }

    // ----------------------------------------------------------------
    // SetProvider / ListProviders — DEFERRED #10
    // ----------------------------------------------------------------

    private static LlmProviderDefaults DefaultsWithAll()
    {
        var clientSettings = new LlmClientSettings
                             {
                                 Groq       = new GroqSettings { Model = "llama-3.3-70b-versatile" },
                                 OpenRouter = new OpenRouterSettings { Model = "anthropic/claude-3.5-sonnet" },
                                 Gemini     = new GeminiSettings { Model = "gemini-2.5-flash" },
                                 Model      = "llama3.1:8b",
                                 Cerebras   = new CerebrasSettings { Model = "llama3.1-8b" }
                             };
        return new LlmProviderDefaults(Options.Create(clientSettings));
    }

    [Fact]
    public void SessionProviderKey_HasStableLiteral()
    {
        Assert.Equal("session_provider", LlmActions.SessionProviderKey);
    }

    [Fact]
    public void SetProvider_AtomicallyUpdatesSession_AndReturnsConfirmation()
    {
        var context = MakeContext();
    //
        LlmActions.SetContext(context);
        LlmActions.SetProviderDefaults(DefaultsWithAll());
    //
        var result = LlmActions.SetProvider("OpenRouter");
    //
        var session = context.CurrentLlmSession;
    //
        Assert.Contains("Switched to OpenRouter",      result);
        Assert.Contains("anthropic/claude-3.5-sonnet", result);
        Assert.Equal("OpenRouter",                     session.Provider);
        Assert.Equal("anthropic/claude-3.5-sonnet",    session.Model);
    }

    
    [Fact]
    public void SetProvider_IsCaseInsensitive()
    {
        var context = MakeContext();
    //
        LlmActions.SetContext(context);
        LlmActions.SetProviderDefaults(DefaultsWithAll());
    //
        var result = LlmActions.SetProvider("openrouter");
    //
        Assert.Contains("Switched to OpenRouter", result);
        Assert.Equal("OpenRouter", context.CurrentLlmSession.Provider);
    }
    

    [Fact]
    public void SetProvider_RejectsUnknownProvider_AndListsAvailable()
    {
        var context = MakeContext();
    //
        LlmActions.SetContext(context);
        LlmActions.SetProviderDefaults(DefaultsWithAll());
    //
        var result = LlmActions.SetProvider("NotAProvider");
    //
        Assert.Contains("Unknown provider", result);
        Assert.Contains("Groq",             result);
        Assert.Contains("OpenRouter",       result);
        Assert.False(context.CurrentLlmSession.HasProvider);
    }

    //
    [Fact]
    public void SetProvider_ReturnsError_WhenProviderIsEmpty()
    {
        var context = MakeContext();
    //
        LlmActions.SetContext(context);
        LlmActions.SetProviderDefaults(DefaultsWithAll());
    //
        var result = LlmActions.SetProvider(string.Empty);
    //
        Assert.Contains("provide a provider name", result);
    }

    //
    [Fact]
    public void SetProvider_ReturnsError_WhenNoDefaultConfigured()
    {
        var context = MakeContext();
    //
        LlmActions.SetContext(context);
        LlmActions.SetProviderDefaults(new LlmProviderDefaults(Options.Create(new LlmClientSettings { 
            Gemini = new GeminiSettings { Model = string.Empty },
            OpenRouter = new OpenRouterSettings { Model = string.Empty },
            Groq = new GroqSettings { Model = string.Empty },
            Cerebras = new CerebrasSettings { Model = string.Empty },
            Model = string.Empty,
            DefaultModel = string.Empty
        })));
    //
        var result = LlmActions.SetProvider("Gemini");
    //
        Assert.Contains("no default model configured", result);
        Assert.False(context.CurrentLlmSession.HasProvider);
    }

    //
    [Fact]
    public void ListProviders_IncludesAllKnownProviders()
    {
        var context = MakeContext();
    //
        LlmActions.SetContext(context);
        LlmActions.SetProviderDefaults(DefaultsWithAll());
    //
        var result = LlmActions.ListProviders();
    //
        Assert.Contains("Groq",       result);
        Assert.Contains("Gemini",     result);
        Assert.Contains("Ollama",     result);
        Assert.Contains("OpenRouter", result);
        Assert.Contains("Cerebras",   result);
    }

    //
    [Fact]
    public void ListProviders_MarksCurrentSessionProvider()
    {
        var context = MakeContext();
        context.SetLlmSession("Gemini", "gemini-2.5-flash");
    //
        LlmActions.SetContext(context);
        LlmActions.SetProviderDefaults(DefaultsWithAll());
    //
        var result = LlmActions.ListProviders();
    //
        Assert.Contains("Gemini (current)", result);
    }

    
    [Fact]
    public void ListProviders_AnnotatesUnconfiguredProviders()
    {
        var context = MakeContext();
    //
        LlmActions.SetContext(context);
        LlmActions.SetProviderDefaults(new LlmProviderDefaults(Options.Create(new LlmClientSettings { 
            Gemini = new GeminiSettings { Model = string.Empty },
            OpenRouter = new OpenRouterSettings { Model = string.Empty },
            Cerebras = new CerebrasSettings { Model = string.Empty },
            Model = string.Empty,
            DefaultModel = string.Empty,
            Groq = new GroqSettings { Model = "llama-3.3-70b-versatile" }
        })));
    //
        var result = LlmActions.ListProviders();
    //
        Assert.Contains("Groq",                    result);
        Assert.Contains("llama-3.3-70b-versatile", result);
        Assert.Contains("not configured",          result);
    }
}



