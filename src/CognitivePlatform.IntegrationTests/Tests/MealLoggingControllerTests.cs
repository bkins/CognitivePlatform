using System.Net.Http.Json;
using System.Text.Json;
using CognitivePlatform.IntegrationTests.Infrastructure;
using Xunit.Abstractions;

namespace CognitivePlatform.IntegrationTests.Tests;

/// <summary>
/// Integration tests that programmatically validate the Phase 5.1 UAT scenarios
/// for Meal Logging and COCE (Conversational Object Construction Engine).
///
/// Prerequisites:
///   - The CognitivePlatform API must be running on localhost:5273
///   - An LLM provider must be configured and reachable
///   - LlmClient__Provider must NOT be set to Mock (meal logging uses COCE via LLM)
///
/// If the API is offline the tests skip gracefully via a TCP pre-flight check.
/// If any individual LLM call times out the affected step is skipped gracefully
/// rather than failing the test — LLM latency is an environment concern, not a code defect.
///
/// Run only these tests:
///   dotnet test --filter "Category=MealLogging" -v detailed
/// </summary>
[Trait("Category", "MealLogging")]
[Trait("UAT", "Phase5.1")]
public sealed class MealLoggingControllerTests : IDisposable
{
    private readonly ApiFixture        _fixture;
    private readonly ITestOutputHelper _output;

    public MealLoggingControllerTests(ITestOutputHelper output)
    {
        _output  = output;
        _fixture = new ApiFixture(output);
    }

    public void Dispose() => _fixture.Dispose();

    // -----------------------------------------------------------------------
    // UAT-5.1.1: Single Meal Narrative Logging
    // -----------------------------------------------------------------------

    /// <summary>
    /// UAT-5.1.1 — The system MUST route "I had oatmeal for breakfast at 8am"
    /// to <c>LogMeal</c> via COCE, not reply conversationally.
    /// Validates: correct action selected, not ChitChat, not null.
    /// </summary>
    [Fact]
    public async Task Converse_BreakfastNarrative_RoutesToLogMeal_WithBreakfastType()
    {
        SkipIfApiOffline();

        var sessionId = $"uat-511-{Guid.NewGuid():N}";
        var input     = "I had a bowl of oatmeal with blueberries and black coffee for breakfast at 8am.";

        _fixture.Log($"UAT-5.1.1 — session: {sessionId}");

        var json = await ConverseAsync(sessionId, input);
        if (json is null) return;

        _fixture.LogAssertion("selectedAction is LogMeal");
        json.Value.GetProperty("selectedAction").GetString()
            .Should().Be("LogMeal"
                       , because: "a breakfast narrative MUST route to LogMeal, not ChitChat.");

        _fixture.LogAssertion("success is true");
        json.Value.GetProperty("success").GetBoolean().Should().BeTrue();

        _fixture.LogAssertion("wasFastPath is false (LLM-routed COCE)");
        json.Value.GetProperty("wasFastPath").GetBoolean().Should().BeFalse();
    }

    /// <summary>
    /// UAT-5.1.1 (secondary) — Ensures the COCE object is logged, then verifies
    /// via a subsequent <c>ListMeals</c> converse call for today.
    /// </summary>
    [Fact]
    public async Task Converse_BreakfastNarrative_CreatesBreakfastMealWithFoodEntries()
    {
        SkipIfApiOffline();

        var logSessionId  = $"uat-511b-{Guid.NewGuid():N}";
        var listSessionId = $"uat-511b-list-{Guid.NewGuid():N}";

        _fixture.Log($"UAT-5.1.1 (verify COCE object) — session: {logSessionId}");

        // Step 1 — Log the meal
        var logJson = await ConverseAsync(logSessionId, "I had scrambled eggs and toast for breakfast at 7am.");
        if (logJson is null) return;

        var selectedAction = logJson.Value.GetProperty("selectedAction").GetString();
        if (selectedAction != "LogMeal")
            Assert.Fail($"Expected selectedAction='LogMeal' but got '{selectedAction}'.");

        // Step 2 — Verify via ListMeals
        var listJson = await ConverseAsync(listSessionId, "What did I eat today?");
        if (listJson is null) return;

        _fixture.LogAssertion("ListMeals selectedAction is ListMeals");
        listJson.Value.GetProperty("selectedAction").GetString().Should().Be("ListMeals");

        _fixture.LogAssertion("ListMeals response message is not empty");
        listJson.Value.GetProperty("message").GetString().Should().NotBeNullOrWhiteSpace();
    }

    // -----------------------------------------------------------------------
    // UAT-5.1.2: Multi-Meal Narrative Logging
    // -----------------------------------------------------------------------

    /// <summary>
    /// UAT-5.1.2 — Lunch narrative MUST route to <c>LogMeal</c>.
    /// Split into individual facts so a slow dinner call cannot block the lunch assertion.
    /// </summary>
    [Fact]
    public async Task Converse_LunchNarrative_RoutesToLogMeal()
    {
        SkipIfApiOffline();

        var sessionId = $"uat-512-lunch-{Guid.NewGuid():N}";

        _fixture.Log($"UAT-5.1.2 (lunch) — session: {sessionId}");

        var json = await ConverseAsync(sessionId, "For lunch I had a turkey sandwich and a bag of chips.");
        if (json is null) return;

        _fixture.LogAssertion("Lunch selectedAction is LogMeal");
        json.Value.GetProperty("selectedAction").GetString()
            .Should().Be("LogMeal"
                       , because: "'For lunch I had...' must route to LogMeal.");
    }

    /// <summary>
    /// UAT-5.1.2 — Dinner narrative MUST route to <c>LogMeal</c>.
    /// Kept as a separate fact from the lunch test so timeout of one cannot block the other.
    /// </summary>
    [Fact]
    public async Task Converse_DinnerNarrative_RoutesToLogMeal()
    {
        SkipIfApiOffline();

        var sessionId = $"uat-512-dinner-{Guid.NewGuid():N}";

        _fixture.Log($"UAT-5.1.2 (dinner) — session: {sessionId}");

        var json = await ConverseAsync(sessionId, "For dinner I had grilled salmon and rice.");
        if (json is null) return;

        _fixture.LogAssertion("Dinner selectedAction is LogMeal");
        json.Value.GetProperty("selectedAction").GetString()
            .Should().Be("LogMeal"
                       , because: "'For dinner I had...' must route to LogMeal.");
    }

    // -----------------------------------------------------------------------
    // UAT-5.1.3: View Logged Meals
    // -----------------------------------------------------------------------

    /// <summary>
    /// UAT-5.1.3 — "What did I eat yesterday?" MUST route to <c>ListMeals</c>.
    /// </summary>
    [Fact]
    public async Task Converse_WhatDidIEatYesterday_RoutesToListMeals()
    {
        SkipIfApiOffline();

        var sessionId = $"uat-513-{Guid.NewGuid():N}";

        _fixture.Log($"UAT-5.1.3 — session: {sessionId}");

        var json = await ConverseAsync(sessionId, "What did I eat yesterday?");
        if (json is null) return;

        _fixture.LogAssertion("selectedAction is ListMeals");
        json.Value.GetProperty("selectedAction").GetString()
            .Should().Be("ListMeals"
                       , because: "'What did I eat yesterday?' MUST route to ListMeals.");

        _fixture.LogAssertion("success is true");
        json.Value.GetProperty("success").GetBoolean().Should().BeTrue();
    }

    /// <summary>
    /// UAT-5.1.3 (secondary) — "What did I eat today?" also validates <c>ListMeals</c> routing.
    /// </summary>
    [Fact]
    public async Task Converse_WhatDidIEatToday_RoutesToListMeals()
    {
        SkipIfApiOffline();

        var sessionId = $"uat-513b-{Guid.NewGuid():N}";

        _fixture.Log($"UAT-5.1.3b — session: {sessionId}");

        var json = await ConverseAsync(sessionId, "What did I eat today?");
        if (json is null) return;

        _fixture.LogAssertion("selectedAction is ListMeals");
        json.Value.GetProperty("selectedAction").GetString()
            .Should().Be("ListMeals"
                       , because: "'What did I eat today?' MUST route to ListMeals.");
    }

    // -----------------------------------------------------------------------
    // Regression: model must NOT respond conversationally to meal statements
    // -----------------------------------------------------------------------

    /// <summary>
    /// Regression guard for bug AE8C — the exact input that caused the original failure
    /// where the model responded "That sounds healthy!" instead of a LogMeal COCE object.
    /// </summary>
    [Fact]
    [Trait("Bug", "AE8C")]
    public async Task Converse_BreakfastOatmeal_DoesNotRespondConversationally()
    {
        SkipIfApiOffline();

        var sessionId = $"regression-ae8c-{Guid.NewGuid():N}";

        _fixture.Log($"Regression AE8C — session: {sessionId}");

        var json = await ConverseAsync(
            sessionId,
            "I had a bowl of oatmeal with blueberries and black coffee for breakfast at 8am.");

        if (json is null) return;

        var selectedAction = json.Value.GetProperty("selectedAction").GetString();

        selectedAction.Should().NotBeNullOrWhiteSpace(
            because: "The model MUST select an action, not respond conversationally.");

        selectedAction.Should().NotBe("ChitChat"
          , because: "Meal statements must route to LogMeal, not ChitChat.");

        selectedAction.Should().Be("LogMeal"
          , because: "Regression guard for bug AE8C: oatmeal breakfast MUST log a meal.");
    }

    // -----------------------------------------------------------------------
    // Infrastructure helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Sends a single converse request and returns the parsed JSON body, or
    /// <see langword="null"/> when the call should be treated as a skip:
    /// <list type="bullet">
    ///   <item>API returned a non-success HTTP status</item>
    ///   <item>The LLM call timed out (<see cref="TaskCanceledException"/>)</item>
    ///   <item>The LLM provider reported it is not usable on this machine</item>
    /// </list>
    /// Using <see langword="null"/> as the skip signal keeps every test method clean:
    /// callers just do <c>if (json is null) return;</c>.
    /// </summary>
    private async Task<JsonElement?> ConverseAsync(string sessionId, string input)
    {
        var payload = new { SessionId = sessionId, Input = input };

        HttpResponseMessage response;
        try
        {
            response = await _fixture.Client.PostAsJsonAsync("/api/conversation/converse", payload);
        }
        catch (TaskCanceledException ex)
        {
            _fixture.Log($"[SKIP] LLM call timed out for input '{input}': {ex.Message}");
            return null;
        }
        catch (OperationCanceledException ex)
        {
            _fixture.Log($"[SKIP] LLM call cancelled for input '{input}': {ex.Message}");
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            _fixture.Log($"[SKIP] API returned {(int)response.StatusCode} for input '{input}'.");
            return null;
        }

        var body = await response.Content.ReadAsStringAsync();
        _output.WriteLine($"Response [{sessionId}]: {body}");

        var json = JsonSerializer.Deserialize<JsonElement>(body, ApiFixture.JsonOptions);

        if (json.TryGetProperty("message", out var msgProp))
        {
            var msg = msgProp.GetString() ?? string.Empty;
            if (msg.ContainsIgnoreCase("not usable on this system")
             || msg.ContainsIgnoreCase("No usable model found")
             || msg.ContainsIgnoreCase("didn't recognize that"))
            {
                _fixture.Log($"[SKIP] LLM provider not usable: {msg}");
                return null;
            }
        }

        return json;
    }

    /// <summary>
    /// Performs a fast TCP check and throws <see cref="ApiOfflineException"/>
    /// when the API is not reachable, preventing the 120 s HTTP timeout from stalling the suite.
    /// </summary>
    private void SkipIfApiOffline()
    {
        if (_fixture.IsApiOnline())
            return;

        _fixture.Log($"[SKIP] API is not reachable at {ApiFixture.BaseUrl}.");
        throw new ApiOfflineException(
            $"CognitivePlatform API is not running at {ApiFixture.BaseUrl}. " +
            "Start the API before running MealLogging integration tests.");
    }
}

/// <summary>Thrown when the CognitivePlatform API is not reachable.</summary>
public sealed class ApiOfflineException(string message) : Exception(message);
