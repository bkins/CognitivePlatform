using CognitivePlatform.Api.Interpreter;
using Moq;

namespace CognitivePlatform.Tests;

public class LlmCapacityRouterTests
{
    private readonly Mock<ILlmRateLimiter> _rateLimiterMock = new();

    // ----------------------------------------------------------------
    // Helpers
    // ----------------------------------------------------------------

    private LlmCapacityRouter Build(params LlmModelConfig[] configs)
        => new(configs, _rateLimiterMock.Object);

    private static LlmModelConfig GroqConfig(int priority = 1, int? requestsPerWindow = null, int? tokensPerWindow = null, TimeSpan? window = null)
        => new()
           {
                   Provider = "groq"
                 , Model    = "llama-3.3-70b-versatile"
                 , Priority = priority
                 , Limits   = new LlmRateLimits
                              {
                                      RequestsPerWindow = requestsPerWindow
                                    , TokensPerWindow   = tokensPerWindow
                                    , Window            = window
                              }
           };

    private static LlmModelConfig GeminiConfig(int priority = 2, int? requestsPerWindow = null, int? tokensPerWindow = null, TimeSpan? window = null)
        => new()
           {
                   Provider = "gemini"
                 , Model    = "gemini-2.0-flash"
                 , Priority = priority
                 , Limits   = new LlmRateLimits
                              {
                                      RequestsPerWindow = requestsPerWindow
                                    , TokensPerWindow   = tokensPerWindow
                                    , Window            = window
                              }
           };

    private static LlmModelConfig OllamaConfig(int priority = 3)
        => new() { Provider = "ollama", Model = "llama3.2", Priority = priority };

    private static LlmUsageInfo Usage(int total) => new() { TotalTokens = total };

    // ----------------------------------------------------------------
    // SelectModel
    // ----------------------------------------------------------------

    [Fact]
    public void SelectModel_Returns_HighestPriorityNonExhaustedModel()
    {
        var router = Build(GroqConfig(priority: 1), GeminiConfig(priority: 2));

        var result = router.SelectModel();

        Assert.Equal("groq", result.Provider);
        Assert.Equal("llama-3.3-70b-versatile", result.Model);
    }

    [Fact]
    public void SelectModel_SkipsExhaustedModel_ReturnsNextWithinSameProvider()
    {
        var groqHigh = new LlmModelConfig
                       {
                               Provider = "groq"
                             , Model    = "llama-3.3-70b-versatile"
                             , Priority = 1
                             , Limits   = new LlmRateLimits { RequestsPerWindow = 2, Window = TimeSpan.FromHours(1) }
                       };
        var groqLow  = new LlmModelConfig
                       {
                               Provider = "groq"
                             , Model    = "mixtral-8x7b"
                             , Priority = 2
                             , Limits   = new LlmRateLimits { RequestsPerWindow = 10, Window = TimeSpan.FromHours(1) }
                       };

        var router = Build(groqHigh, groqLow);

        // Exhaust the high-priority Groq model
        var highId = new LlmModelId("groq", "llama-3.3-70b-versatile");
        router.RecordUsage(highId, Usage(1));
        router.RecordUsage(highId, Usage(1));

        var result = router.SelectModel();

        Assert.Equal("groq",         result.Provider);
        Assert.Equal("mixtral-8x7b", result.Model);
    }

    [Fact]
    public void SelectModel_SkipsExhaustedProvider_ReturnsModelFromNextProvider()
    {
        var router = Build(GroqConfig(priority: 1, requestsPerWindow: 1, window: TimeSpan.FromHours(1))
                         , GeminiConfig(priority: 2));

        var groqId = new LlmModelId("groq", "llama-3.3-70b-versatile");
        router.RecordUsage(groqId, Usage(100));

        var result = router.SelectModel();

        Assert.Equal("gemini",           result.Provider);
        Assert.Equal("gemini-2.0-flash", result.Model);
    }

    [Fact]
    public void SelectModel_SkipsProvider_WhenRateLimiterReportsExhausted()
    {
        // Local counters are not exhausted — no limits configured — but the rate limiter
        // reports Groq as exhausted (e.g. a 429 was recorded from response headers).
        _rateLimiterMock
            .Setup(limiter => limiter.IsExhausted("groq"))
            .Returns(true);

        var router = Build(GroqConfig(priority: 1), GeminiConfig(priority: 2));

        var result = router.SelectModel();

        Assert.Equal("gemini",           result.Provider);
        Assert.Equal("gemini-2.0-flash", result.Model);
    }

    [Fact]
    public void SelectModel_Throws_WhenAllModelsAreExhausted()
    {
        var router = Build(GroqConfig(priority: 1, requestsPerWindow: 1, window: TimeSpan.FromHours(1))
                         , GeminiConfig(priority: 2, requestsPerWindow: 1, window: TimeSpan.FromHours(1)));

        var groqId   = new LlmModelId("groq",   "llama-3.3-70b-versatile");
        var geminiId = new LlmModelId("gemini",  "gemini-2.0-flash");

        router.RecordUsage(groqId,   Usage(1));
        router.RecordUsage(geminiId, Usage(1));

        Assert.Throws<LlmCapacityExceededException>(() => router.SelectModel());
    }

    [Fact]
    public void SelectModel_Throws_WhenNoModelsConfigured()
    {
        var router = Build();

        Assert.Throws<LlmCapacityExceededException>(() => router.SelectModel());
    }

    // ----------------------------------------------------------------
    // RecordUsage — counter increments and window rollover
    // ----------------------------------------------------------------

    [Fact]
    public void RecordUsage_IncrementsRequestsAndTokens()
    {
        var router = Build(GroqConfig(priority: 1, requestsPerWindow: 10, tokensPerWindow: 1000, window: TimeSpan.FromHours(1)));

        var groqId = new LlmModelId("groq", "llama-3.3-70b-versatile");
        router.RecordUsage(groqId, Usage(300));
        router.RecordUsage(groqId, Usage(200));

        // 2 requests, 500 total tokens — model should not yet be exhausted
        var result = router.SelectModel();

        Assert.Equal("groq", result.Provider);
    }

    [Fact]
    public void RecordUsage_ExhaustsModel_WhenRequestLimitReached()
    {
        var router = Build(GroqConfig(priority: 1, requestsPerWindow: 2, window: TimeSpan.FromHours(1))
                         , GeminiConfig(priority: 2));

        var groqId = new LlmModelId("groq", "llama-3.3-70b-versatile");
        router.RecordUsage(groqId, Usage(1));
        router.RecordUsage(groqId, Usage(1));

        var result = router.SelectModel();

        Assert.Equal("gemini", result.Provider);
    }

    [Fact]
    public void RecordUsage_ResetsCounters_AfterWindowRollover()
    {
        // Use a window that has already elapsed so the first RecordUsage triggers a rollover
        var config = new LlmModelConfig
                     {
                             Provider = "groq"
                           , Model    = "llama-3.3-70b-versatile"
                           , Priority = 1
                           , Limits   = new LlmRateLimits
                                        {
                                                RequestsPerWindow = 1
                                              , Window            = TimeSpan.FromMilliseconds(1)
                                        }
                     };

        var router = Build(config);
        var groqId = new LlmModelId("groq", "llama-3.3-70b-versatile");

        // Exhaust the model
        router.RecordUsage(groqId, Usage(1));

        // Let the window expire
        System.Threading.Thread.Sleep(10);

        // After window rollover, the model should be available again
        var result = router.SelectModel();

        Assert.Equal("groq", result.Provider);
    }

    // ----------------------------------------------------------------
    // RecordRateLimits — delegates to ILlmRateLimiter
    // ----------------------------------------------------------------

    [Fact]
    public void RecordRateLimits_DelegatesSnapshotToRateLimiter()
    {
        var router   = Build(GroqConfig());
        var groqId   = new LlmModelId("groq", "llama-3.3-70b-versatile");
        var snapshot = new LlmRateLimitSnapshot { Provider = "groq", RequestLimit = 100, RequestsRemaining = 50 };

        router.RecordRateLimits(groqId, snapshot);

        _rateLimiterMock.Verify(limiter => limiter.Update(It.Is<LlmResponseMetadata>(metadata =>
                                                              metadata.ProviderId  == "groq"
                                                           && metadata.RateLimits  == snapshot))
                              , Times.Once);
    }

    // ----------------------------------------------------------------
    // Unknown model — no-op (does not throw)
    // ----------------------------------------------------------------

    [Fact]
    public void RecordUsage_DoesNotThrow_WhenModelIdIsNotConfigured()
    {
        var router = Build(GroqConfig());
        var unknownId = new LlmModelId("unknown-provider", "unknown-model");

        var exception = Record.Exception(() => router.RecordUsage(unknownId, Usage(100)));

        Assert.Null(exception);
    }

    // ----------------------------------------------------------------
    // SelectModel(TaskComplexity) — ENH-19 tier-aware selection
    // ----------------------------------------------------------------

    private static LlmModelConfig HeavyModelConfig(int priority = 1)
        => new()
           {
                   Provider = "gemini"
                 , Model    = "gemini-2.0-pro"
                 , Priority = priority
                 , Tier     = TaskComplexity.Heavy
           };

    private static LlmModelConfig StandardModelConfig(int priority = 2)
        => new()
           {
                   Provider = "groq"
                 , Model    = "llama-3.3-70b-versatile"
                 , Priority = priority
                 , Tier     = TaskComplexity.Standard
           };

    private static LlmModelConfig LightModelConfig(int priority = 3)
        => new()
           {
                   Provider = "gemini"
                 , Model    = "gemini-2.0-flash-lite"
                 , Priority = priority
                 , Tier     = TaskComplexity.Light
           };

    [Fact]
    public void SelectModel_HeavyComplexity_PrefersHeavyTierModel()
    {
        var router = Build(HeavyModelConfig(priority: 1), StandardModelConfig(priority: 2));

        var result = router.SelectModel(TaskComplexity.Heavy);

        Assert.Equal("gemini",          result.ModelId.Provider);
        Assert.Equal("gemini-2.0-pro",  result.ModelId.Model);
        Assert.Null(result.TierDowngradeNote);
    }

    [Fact]
    public void SelectModel_HeavyComplexity_FallsBackToStandard_WhenHeavyExhausted()
    {
        var heavy    = HeavyModelConfig(priority: 1);
        heavy = new LlmModelConfig
                {
                        Provider = heavy.Provider
                      , Model    = heavy.Model
                      , Priority = heavy.Priority
                      , Tier     = heavy.Tier
                      , Limits   = new LlmRateLimits { RequestsPerWindow = 1, Window = TimeSpan.FromHours(1) }
                };

        var router   = Build(heavy, StandardModelConfig(priority: 2));
        var heavyId  = new LlmModelId(heavy.Provider, heavy.Model);
        router.RecordUsage(heavyId, Usage(1));

        var result = router.SelectModel(TaskComplexity.Heavy);

        Assert.Equal("groq",                     result.ModelId.Provider);
        Assert.Equal("llama-3.3-70b-versatile",  result.ModelId.Model);
        Assert.NotNull(result.TierDowngradeNote);
        Assert.Contains("available", result.TierDowngradeNote, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SelectModel_StandardComplexity_SkipsLightTierModels()
    {
        var router = Build(LightModelConfig(priority: 1), StandardModelConfig(priority: 2));

        var result = router.SelectModel(TaskComplexity.Standard);

        Assert.Equal("groq",                    result.ModelId.Provider);
        Assert.Equal("llama-3.3-70b-versatile", result.ModelId.Model);
        Assert.Null(result.TierDowngradeNote);
    }

    [Fact]
    public void SelectModel_LightComplexity_AcceptsAnyAvailableModel()
    {
        var router = Build(LightModelConfig(priority: 1), StandardModelConfig(priority: 2), HeavyModelConfig(priority: 3));

        var result = router.SelectModel(TaskComplexity.Light);

        Assert.Equal("gemini",               result.ModelId.Provider);
        Assert.Equal("gemini-2.0-flash-lite", result.ModelId.Model);
        Assert.Null(result.TierDowngradeNote);
    }

    [Fact]
    public void SelectModel_WithComplexity_Throws_WhenAllModelsExhausted()
    {
        var heavy    = new LlmModelConfig
                       {
                               Provider = "gemini"
                             , Model    = "gemini-2.0-pro"
                             , Priority = 1
                             , Tier     = TaskComplexity.Heavy
                             , Limits   = new LlmRateLimits { RequestsPerWindow = 1, Window = TimeSpan.FromHours(1) }
                       };
        var standard = new LlmModelConfig
                       {
                               Provider = "groq"
                             , Model    = "llama-3.3-70b-versatile"
                             , Priority = 2
                             , Tier     = TaskComplexity.Standard
                             , Limits   = new LlmRateLimits { RequestsPerWindow = 1, Window = TimeSpan.FromHours(1) }
                       };

        var router   = Build(heavy, standard);
        router.RecordUsage(new LlmModelId(heavy.Provider,    heavy.Model),    Usage(1));
        router.RecordUsage(new LlmModelId(standard.Provider, standard.Model), Usage(1));

        Assert.Throws<LlmCapacityExceededException>(() => router.SelectModel(TaskComplexity.Heavy));
    }

    [Fact]
    public void LlmModelConfig_Tier_DefaultsToStandard_WhenNotConfigured()
    {
        var config = new LlmModelConfig { Provider = "groq", Model = "llama-3", Priority = 1 };

        Assert.Equal(TaskComplexity.Standard, config.Tier);
    }
}
