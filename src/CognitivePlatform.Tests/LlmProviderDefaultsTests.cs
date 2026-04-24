using CognitivePlatform.Api.Interpreter;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CognitivePlatform.Tests;

public class LlmProviderDefaultsTests
{
    [Fact]
    public void BindsFromConfiguration_PopulatesEveryKnownProvider()
    {
        var dict = new Dictionary<string, string?>
                   {
                           ["Llm:Defaults:Ollama"]     = "llama3.1:8b"
                         , ["Llm:Defaults:Groq"]       = "llama-3.3-70b-versatile"
                         , ["Llm:Defaults:Gemini"]     = "gemini-2.0-flash"
                         , ["Llm:Defaults:OpenRouter"] = "anthropic/claude-3.5-sonnet"
                         , ["Llm:Defaults:Cerebras"]   = "llama3.1-8b"
                   };

        var defaults = BindDefaults(dict);

        Assert.Equal("llama3.1:8b",                    defaults.Ollama);
        Assert.Equal("llama-3.3-70b-versatile",        defaults.Groq);
        Assert.Equal("gemini-2.0-flash",               defaults.Gemini);
        Assert.Equal("anthropic/claude-3.5-sonnet",    defaults.OpenRouter);
        Assert.Equal("llama3.1-8b",                    defaults.Cerebras);
    }

    [Fact]
    public void For_ReturnsConfiguredValue_ForKnownProvider()
    {
        var defaults = new LlmProviderDefaults { OpenRouter = "anthropic/claude-3.5-sonnet" };

        Assert.Equal("anthropic/claude-3.5-sonnet", defaults.For(LlmProvider.OpenRouter));
    }

    [Fact]
    public void For_ReturnsEmpty_ForUnconfiguredProvider()
    {
        var defaults = new LlmProviderDefaults();

        Assert.Equal(string.Empty, defaults.For(LlmProvider.Gemini));
    }

    [Fact]
    public void BindsFromConfiguration_LeavesMissingProviderEmpty()
    {
        var dict = new Dictionary<string, string?>
                   {
                           ["Llm:Defaults:Groq"] = "llama-3.3-70b-versatile"
                   };

        var defaults = BindDefaults(dict);

        Assert.Equal("llama-3.3-70b-versatile", defaults.Groq);
        Assert.Equal(string.Empty,               defaults.OpenRouter);
        Assert.Equal(string.Empty,               defaults.Gemini);
        Assert.Equal(string.Empty,               defaults.Ollama);
        Assert.Equal(string.Empty,               defaults.Cerebras);
    }

    private static LlmProviderDefaults BindDefaults(Dictionary<string, string?> dict)
    {
        var config = new ConfigurationBuilder()
                    .AddInMemoryCollection(dict)
                    .Build();

        var services = new ServiceCollection();
        services.Configure<LlmProviderDefaults>(config.GetSection("Llm:Defaults"));
        var provider = services.BuildServiceProvider();

        return provider.GetRequiredService<IOptions<LlmProviderDefaults>>().Value;
    }
}
