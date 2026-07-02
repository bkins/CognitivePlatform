using CognitivePlatform.Api.Interpreter;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CognitivePlatform.Tests;

public class LlmProviderDefaultsTests
{

    [Fact]
    public void For_ReturnsConfiguredValue_ForKnownProvider()
    {
        var settings = new LlmClientSettings { OpenRouter = new OpenRouterSettings { Model = "anthropic/claude-3.5-sonnet" } };
        var defaults = new LlmProviderDefaults(Options.Create(settings));

        Assert.Equal("anthropic/claude-3.5-sonnet", defaults.For(LlmProvider.OpenRouter));
    }

    [Fact]
    public void For_ReturnsEmpty_ForUnconfiguredProvider()
    {
        var settings = new LlmClientSettings { 
            Gemini = new GeminiSettings { Model = string.Empty },
            OpenRouter = new OpenRouterSettings { Model = string.Empty },
            Groq = new GroqSettings { Model = string.Empty },
            Cerebras = new CerebrasSettings { Model = string.Empty },
            Model = string.Empty,
            DefaultModel = string.Empty
        };
        var defaults = new LlmProviderDefaults(Options.Create(settings));

        Assert.Equal(string.Empty, defaults.For(LlmProvider.Gemini));
    }
    //
    [Fact]
    public void BindsFromConfiguration_LeavesMissingProviderEmpty()
    {
        var dict = new Dictionary<string, string?>
                   {
                           ["Llm:Clients:Groq:Model"] = "llama-3.3-70b-versatile"
                   };

        var defaults = BindDefaults(dict);

        Assert.Equal("llama-3.3-70b-versatile", defaults.For(LlmProvider.Groq));
        Assert.Equal("openai/gpt-4o-mini",       defaults.For(LlmProvider.OpenRouter));
        Assert.Equal("llama-3.3-70b-versatile",  defaults.For(LlmProvider.Gemini));
        Assert.Equal("qwen2.5:14b",              defaults.For(LlmProvider.Ollama));
        Assert.Equal("llama3.1-8b",              defaults.For(LlmProvider.Cerebras));
    }

    private static LlmProviderDefaults BindDefaults(Dictionary<string, string?> dict)
    {
        var config = new ConfigurationBuilder()
                    .AddInMemoryCollection(dict)
                    .Build();

        var services = new ServiceCollection();
        services.Configure<LlmClientSettings>(config.GetSection("Llm:Clients"));
        services.AddTransient<LlmProviderDefaults>();
        var provider = services.BuildServiceProvider();

        return provider.GetRequiredService<LlmProviderDefaults>();
    }
}

