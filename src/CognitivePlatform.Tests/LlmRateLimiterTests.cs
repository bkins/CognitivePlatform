using CognitivePlatform.Api.Interpreter;

namespace CognitivePlatform.Tests;

public class LlmRateLimiterTests
{
    private readonly InMemoryLlmRateLimiter _rateLimiter = new();

    private static LlmResponseMetadata MetadataFor(string provider, int requestsRemaining, int tokensRemaining = 5_000
                                                  , int requestLimit = 100, int tokenLimit = 10_000)
        => new()
           {
                   ProviderId = provider
                 , RateLimits = new LlmRateLimitSnapshot
                                {
                                        Provider         = provider
                                      , RequestLimit      = requestLimit
                                      , RequestsRemaining = requestsRemaining
                                      , TokenLimit        = tokenLimit
                                      , TokensRemaining   = tokensRemaining
                                }
           };

    // ----------------------------------------------------------------
    // IsExhausted
    // ----------------------------------------------------------------

    [Fact]
    public void IsExhausted_ReturnsFalse_WhenNoSnapshotRecordedYet()
    {
        Assert.False(_rateLimiter.IsExhausted("Groq"));
    }

    [Fact]
    public void IsExhausted_ReturnsFalse_WhenBothRequestsAndTokensAreAvailable()
    {
        _rateLimiter.Update(MetadataFor("Groq", requestsRemaining: 50, tokensRemaining: 5_000));

        Assert.False(_rateLimiter.IsExhausted("Groq"));
    }

    [Fact]
    public void IsExhausted_ReturnsTrue_WhenRequestsRemainingIsZero()
    {
        _rateLimiter.Update(MetadataFor("Groq", requestsRemaining: 0, tokensRemaining: 5_000));

        Assert.True(_rateLimiter.IsExhausted("Groq"));
    }

    [Fact]
    public void IsExhausted_ReturnsTrue_WhenTokensRemainingIsZero()
    {
        _rateLimiter.Update(MetadataFor("Groq", requestsRemaining: 50, tokensRemaining: 0));

        Assert.True(_rateLimiter.IsExhausted("Groq"));
    }

    [Fact]
    public void IsExhausted_IsCaseInsensitive_ForProviderName()
    {
        _rateLimiter.Update(MetadataFor("Groq", requestsRemaining: 0));

        Assert.True(_rateLimiter.IsExhausted("groq"));
        Assert.True(_rateLimiter.IsExhausted("GROQ"));
    }

    [Fact]
    public void IsExhausted_IsIndependentPerProvider()
    {
        _rateLimiter.Update(MetadataFor("Groq", requestsRemaining: 0));

        // Gemini has no snapshot yet -> not exhausted (optimistic)
        Assert.True( _rateLimiter.IsExhausted("Groq"));
        Assert.False(_rateLimiter.IsExhausted("Gemini"));
    }

    // ----------------------------------------------------------------
    // Update
    // ----------------------------------------------------------------

    [Fact]
    public void Update_IgnoresEmptyProviderId()
    {
        _rateLimiter.Update(new LlmResponseMetadata
                            {
                                    ProviderId = string.Empty
                                  , RateLimits = new LlmRateLimitSnapshot { RequestsRemaining = 0 }
                            });

        // Still returns false (not exhausted) because nothing was stored
        Assert.False(_rateLimiter.IsExhausted(string.Empty));
    }

    [Fact]
    public void Update_ReplacesExistingSnapshotForSameProvider()
    {
        _rateLimiter.Update(MetadataFor("Groq", requestsRemaining: 0));

        // Simulate window reset
        _rateLimiter.Update(MetadataFor("Groq", requestsRemaining: 100, tokensRemaining: 10_000));

        Assert.False(_rateLimiter.IsExhausted("Groq"));
    }

    // ----------------------------------------------------------------
    // GetLatest
    // ----------------------------------------------------------------

    [Fact]
    public void GetLatest_ReturnsEmpty_WhenNoSnapshotRecorded()
    {
        var snapshot = _rateLimiter.GetLatest("Groq");

        Assert.False(snapshot.HasData);
        Assert.Equal(LlmRateLimitSnapshot.Empty, snapshot);
    }

    [Fact]
    public void GetLatest_ReturnsLastUpdatedSnapshot()
    {
        _rateLimiter.Update(MetadataFor("Groq", requestsRemaining: 75, tokensRemaining: 8_000));

        var snapshot = _rateLimiter.GetLatest("Groq");

        Assert.Equal(75,    snapshot.RequestsRemaining);
        Assert.Equal(8_000, snapshot.TokensRemaining);
    }
}
