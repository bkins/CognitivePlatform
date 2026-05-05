using CognitivePlatform.Api.Interpreter;

namespace CognitivePlatform.Tests;

public class LlmRateLimiterTests
{
    private readonly LlmRateLimiter _rateLimiter = new();

    // ----------------------------------------------------------------
    // CanSend
    // ----------------------------------------------------------------

    [Fact]
    public void CanSend_ReturnsTrue_WhenNoSnapshotRecordedYet()
    {
        Assert.True(_rateLimiter.CanSend("Groq"));
    }

    [Fact]
    public void CanSend_ReturnsTrue_WhenBothRequestsAndTokensAreAvailable()
    {
        _rateLimiter.UpdateFromSnapshot(new LlmRateLimitSnapshot
                                        {
                                                Provider         = "Groq"
                                              , RequestLimit      = 100
                                              , RequestsRemaining = 50
                                              , TokenLimit        = 10_000
                                              , TokensRemaining   = 5_000
                                        });

        Assert.True(_rateLimiter.CanSend("Groq"));
    }

    [Fact]
    public void CanSend_ReturnsFalse_WhenRequestsRemainingIsZero()
    {
        _rateLimiter.UpdateFromSnapshot(new LlmRateLimitSnapshot
                                        {
                                                Provider         = "Groq"
                                              , RequestLimit      = 100
                                              , RequestsRemaining = 0
                                              , TokenLimit        = 10_000
                                              , TokensRemaining   = 5_000
                                        });

        Assert.False(_rateLimiter.CanSend("Groq"));
    }

    [Fact]
    public void CanSend_ReturnsFalse_WhenTokensRemainingIsZero()
    {
        _rateLimiter.UpdateFromSnapshot(new LlmRateLimitSnapshot
                                        {
                                                Provider         = "Groq"
                                              , RequestLimit      = 100
                                              , RequestsRemaining = 50
                                              , TokenLimit        = 10_000
                                              , TokensRemaining   = 0
                                        });

        Assert.False(_rateLimiter.CanSend("Groq"));
    }

    [Fact]
    public void CanSend_IsCaseInsensitive_ForProviderName()
    {
        _rateLimiter.UpdateFromSnapshot(new LlmRateLimitSnapshot
                                        {
                                                Provider         = "Groq"
                                              , RequestLimit      = 100
                                              , RequestsRemaining = 0
                                        });

        Assert.False(_rateLimiter.CanSend("groq"));
        Assert.False(_rateLimiter.CanSend("GROQ"));
    }

    [Fact]
    public void CanSend_IsIndependentPerProvider()
    {
        _rateLimiter.UpdateFromSnapshot(new LlmRateLimitSnapshot
                                        {
                                                Provider         = "Groq"
                                              , RequestLimit      = 100
                                              , RequestsRemaining = 0   // exhausted
                                        });

        // Gemini has no snapshot yet → optimistic
        Assert.False(_rateLimiter.CanSend("Groq"));
        Assert.True( _rateLimiter.CanSend("Gemini"));
    }

    // ----------------------------------------------------------------
    // UpdateFromSnapshot
    // ----------------------------------------------------------------

    [Fact]
    public void UpdateFromSnapshot_IgnoresEmptyProviderName()
    {
        _rateLimiter.UpdateFromSnapshot(new LlmRateLimitSnapshot
                                        {
                                                Provider         = string.Empty
                                              , RequestsRemaining = 0
                                        });

        // Still returns true because nothing was stored
        Assert.True(_rateLimiter.CanSend(string.Empty));
    }

    [Fact]
    public void UpdateFromSnapshot_ReplacesExistingSnapshotForSameProvider()
    {
        _rateLimiter.UpdateFromSnapshot(new LlmRateLimitSnapshot
                                        {
                                                Provider         = "Groq"
                                              , RequestLimit      = 100
                                              , RequestsRemaining = 0   // exhausted
                                        });

        // Simulate window reset
        _rateLimiter.UpdateFromSnapshot(new LlmRateLimitSnapshot
                                        {
                                                Provider         = "Groq"
                                              , RequestLimit      = 100
                                              , RequestsRemaining = 100  // fully reset
                                              , TokenLimit        = 10_000
                                              , TokensRemaining   = 10_000
                                        });

        Assert.True(_rateLimiter.CanSend("Groq"));
    }

    // ----------------------------------------------------------------
    // GetCurrentSnapshot
    // ----------------------------------------------------------------

    [Fact]
    public void GetCurrentSnapshot_ReturnsEmpty_WhenNoSnapshotRecorded()
    {
        var snapshot = _rateLimiter.GetCurrentSnapshot("Groq");

        Assert.False(snapshot.HasData);
        Assert.Equal(LlmRateLimitSnapshot.Empty, snapshot);
    }

    [Fact]
    public void GetCurrentSnapshot_ReturnsLastUpdatedSnapshot()
    {
        var expected = new LlmRateLimitSnapshot
                       {
                               Provider         = "Groq"
                             , RequestLimit      = 100
                             , RequestsRemaining = 75
                             , TokenLimit        = 10_000
                             , TokensRemaining   = 8_000
                       };

        _rateLimiter.UpdateFromSnapshot(expected);

        var snapshot = _rateLimiter.GetCurrentSnapshot("Groq");

        Assert.Equal(expected.RequestsRemaining, snapshot.RequestsRemaining);
        Assert.Equal(expected.TokensRemaining,   snapshot.TokensRemaining);
    }
}
