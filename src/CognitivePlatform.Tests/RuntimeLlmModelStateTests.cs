using CognitivePlatform.Api.Avails;
using Xunit;

namespace CognitivePlatform.Tests;

public class RuntimeLlmModelStateTests
{
    [Fact]
    public void SetEffectiveModel_PreservesConfiguredModel_ForNextStartup()
    {
        var state = new RuntimeLlmModelState();

        state.SetConfiguredModel("gemini-3.1-pro-preview");
        state.SetEffectiveModel("gemini-2.5-flash");

        Assert.Equal("gemini-3.1-pro-preview", state.ConfiguredModel);
        Assert.Equal("gemini-2.5-flash", state.EffectiveModel);
    }
}
