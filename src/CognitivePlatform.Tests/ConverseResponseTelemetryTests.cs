using CognitivePlatform.Api.Contracts;
using Xunit;

namespace CognitivePlatform.Tests;

public class ConverseResponseTelemetryTests
{
    [Fact]
    public void ConverseResponse_DefaultsProviderAndModelToNull()
    {
        var response = new ConverseResponse();

        Assert.Null(response.Provider);
        Assert.Null(response.Model);
        Assert.False(response.WasFastPath);
    }

    [Fact]
    public void ConverseResponse_StoresProviderAndModel()
    {
        var response = new ConverseResponse
        {
            Provider = "Groq",
            Model = "llama-3.3-70b-versatile",
            WasFastPath = false
        };

        Assert.Equal("Groq", response.Provider);
        Assert.Equal("llama-3.3-70b-versatile", response.Model);
        Assert.False(response.WasFastPath);
    }
}
