using CognitivePlatform.Api.Actions;
using CognitivePlatform.Api.Avails;
using CognitivePlatform.Api.Avails.Models;
using CognitivePlatform.Api.Conversation;

namespace CognitivePlatform.Tests;

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
    public void SetModel_StoresSessionModelKey_WhenModelIsUsable()
    {
        var context = MakeContext();
        var catalog = CatalogWith(Usable("llama3.1-8b"));

        LlmActions.SetContext(context);
        LlmActions.SetCatalog(catalog);

        var result = LlmActions.SetModel("llama3.1-8b");

        Assert.Contains("llama3.1-8b", result);
        Assert.Equal("llama3.1-8b", context.Metadata[LlmActions.SessionModelKey]);
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
        Assert.Equal("LLaMa3.1-8B", context.Metadata[LlmActions.SessionModelKey]);
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
        Assert.False(context.Metadata.ContainsKey(LlmActions.SessionModelKey));
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
        Assert.False(context.Metadata.ContainsKey(LlmActions.SessionModelKey));
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
        context.Metadata[LlmActions.SessionModelKey] = "model-a";
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
}
