using CognitivePlatform.Api.Conversation;
using Microsoft.Extensions.Configuration;

namespace CognitivePlatform.Tests;

public class ConversationContextOptionsBindingTests
{
    [Fact]
    public void Defaults_Apply_WhenNoConfigSectionPresent()
    {
        var configuration = new ConfigurationBuilder().Build();
        var bound         = configuration.GetSection("ConversationContext").Get<ConversationContextOptions>()
                         ?? new ConversationContextOptions();

        Assert.Equal(50,   bound.MaxTurnHistory);
        Assert.Equal(4096, bound.MaxTurnMessageLength);
    }

    [Fact]
    public void Binds_BothCaps_FromConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
                                   {
                                           ["ConversationContext:MaxTurnHistory"]       = "12"
                                         , ["ConversationContext:MaxTurnMessageLength"] = "256"
                                   })
            .Build();

        var bound = configuration.GetSection("ConversationContext").Get<ConversationContextOptions>()!;

        Assert.Equal(12,  bound.MaxTurnHistory);
        Assert.Equal(256, bound.MaxTurnMessageLength);
    }

    [Fact]
    public void TruncationMarker_HasStableLiteral()
    {
        Assert.Equal("...[truncated for context]", ConversationContextOptions.TruncationMarker);
    }
}
