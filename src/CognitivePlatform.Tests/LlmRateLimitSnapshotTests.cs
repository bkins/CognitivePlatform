using CognitivePlatform.Api.Interpreter;

namespace CognitivePlatform.Tests;

public class LlmRateLimitSnapshotTests
{
    [Fact]
    public void HasData_ReturnsFalse_ForEmptySingleton()
    {
        Assert.False(LlmRateLimitSnapshot.Empty.HasData);
    }

    [Fact]
    public void HasData_ReturnsTrue_WhenRequestLimitIsSet()
    {
        var snapshot = new LlmRateLimitSnapshot { RequestLimit = 100 };
        Assert.True(snapshot.HasData);
    }

    [Fact]
    public void HasData_ReturnsTrue_WhenTokenLimitIsSet()
    {
        var snapshot = new LlmRateLimitSnapshot { TokenLimit = 10_000 };
        Assert.True(snapshot.HasData);
    }

    [Fact]
    public void RequestsUsed_IsLimitMinusRemaining()
    {
        var snapshot = new LlmRateLimitSnapshot { RequestLimit = 100, RequestsRemaining = 75 };
        Assert.Equal(25, snapshot.RequestsUsed);
    }

    [Fact]
    public void TokensUsed_IsLimitMinusRemaining()
    {
        var snapshot = new LlmRateLimitSnapshot { TokenLimit = 10_000, TokensRemaining = 7_500 };
        Assert.Equal(2_500, snapshot.TokensUsed);
    }

    [Fact]
    public void RequestUsagePercent_IsComputedCorrectly()
    {
        var snapshot = new LlmRateLimitSnapshot { RequestLimit = 100, RequestsRemaining = 25 };
        Assert.Equal(75.0, snapshot.RequestUsagePercent);
    }

    [Fact]
    public void TokenUsagePercent_ReturnsZero_WhenLimitIsZero()
    {
        var snapshot = new LlmRateLimitSnapshot { TokenLimit = 0, TokensRemaining = 0 };
        Assert.Equal(0, snapshot.TokenUsagePercent);
    }
}
