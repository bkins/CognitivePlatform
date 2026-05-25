using System.Reflection;
using CognitivePlatform.Api.Attributes;
using CognitivePlatform.Api.Avails;
using CognitivePlatform.Api.Avails.Models;
using CognitivePlatform.Api.Conversation;
using CognitivePlatform.Api.Interpreter;
using CognitivePlatform.Api.Models;
using CognitivePlatform.Api.Registry.Capabilities;
using CognitivePlatform.Api.SystemPromptLogging;
using CognitivePlatform.Api.Telemetry;
using Moq;

namespace CognitivePlatform.Tests;

// BUG-34: LlmInterpreter null-safety regression tests.
// The extraReason line accessed modelInfo.FailureReason without a null check,
// crashing with NullReferenceException when the requested model was absent
// from the catalog. These tests verify both the null-model and the unusable-model
// paths return a meaningful result rather than throwing.
public class LlmInterpreterTests
{
    private readonly Mock<ICapabilityRegistry> _registryMock  = new();
    private readonly Mock<ITelemetrySink>  _telemetryMock = new();
    private readonly Mock<ILlmRouter>      _routerMock    = new();
    private readonly Mock<IPromptLogger>   _promptLogMock = new();

    private LlmInterpreter BuildInterpreter( LlmModelCatalog   catalog
                                           , LlmClientSettings? settings = null )
    {
        _registryMock.Setup(registry => registry.GetAll())
                     .Returns(Array.Empty<ActionMetadata>());

        return new LlmInterpreter( _registryMock.Object
                                 , _telemetryMock.Object
                                 , _routerMock.Object
                                 , catalog
                                 , settings ?? new LlmClientSettings()
                                 , _promptLogMock.Object );
    }

    [Fact]
    public async Task InterpretWithContext_WhenModelNotInCatalog_ReturnsNoModelResult_WithoutThrowingNullRef()
    {
        // Arrange: empty catalog so FirstOrDefault returns null → modelInfo is null
        var emptyCatalog = new LlmModelCatalog();
        var interpreter  = BuildInterpreter(emptyCatalog);
        var context      = new ConversationContext("test-session");

        // Act: previously threw NullReferenceException at the extraReason line
        var result = await interpreter.InterpretWithContext("what time is it", context);

        // Assert: returns a structured failure rather than an unhandled exception
        Assert.Equal(InterpreterFailureType.NoMatchingAction, result.FailureType);
        Assert.NotNull(result.Reason);
    }

    // EPIC-08: Action catalog enrichment — catalog format and parse-path tests.
    // These tests verify that the enriched catalog sends the right signals to the
    // LLM and that the interpreter correctly parses the expected responses.

    // (a) Journal position-number convention: "show third journal entry" must
    //     resolve to GetJournalEntry with entryReference = "3".
    [Fact]
    public void ParseModelResponse_ThirdJournalEntry_ResolvesToPosition3()
    {
        const string raw = """
                           {
                             "actionName": "GetJournalEntry",
                             "parameters": { "entryReference": "3" },
                             "reason": "User asked for the third journal entry.",
                             "failureType": "None"
                           }
                           """;

        var actions = new[]
        {
            new ActionMetadata
            {
                    Name        = "GetJournalEntry"
                  , Description = "Retrieves a journal entry by position."
                  , Parameters  = new List<ParameterMetadata>
                                  {
                                      new() { Name = "entryReference", IsOptional = false, AllowEmpty = false }
                                  }
            }
        };

        var result = LlmInterpreter.ParseModelResponse(raw, actions);

        Assert.Equal("GetJournalEntry",                       result.ActionName);
        Assert.Equal("3",                                     result.Parameters["entryReference"]);
        Assert.Equal(InterpreterFailureType.None,             result.FailureType);
    }

    // (b) Calendar routing: "what's on my calendar this week" must resolve to
    //     a calendar action, not a task action.
    [Fact]
    public void ParseModelResponse_CalendarThisWeek_ResolvesToCalendarAction()
    {
        const string raw = """
                           {
                             "actionName": "GetEventsForDate",
                             "parameters": { "date": "2026-05-11" },
                             "reason": "User asked about calendar events this week.",
                             "failureType": "None"
                           }
                           """;

        var actions = new[]
        {
            new ActionMetadata
            {
                    Name        = "GetEventsForDate"
                  , Description = "Shows calendar events for a specific date."
                  , Parameters  = new List<ParameterMetadata>
                                  {
                                      new() { Name = "date", IsOptional = false, AllowEmpty = false }
                                  }
            }
            , new ActionMetadata
            {
                    Name        = "ListTasks"
                  , Description = "List tasks."
                  , Parameters  = new List<ParameterMetadata>()
            }
        };

        var result = LlmInterpreter.ParseModelResponse(raw, actions);

        Assert.Equal("GetEventsForDate",              result.ActionName);
        Assert.NotEqual("ListTasks",                  result.ActionName);
        Assert.Equal(InterpreterFailureType.None,     result.FailureType);
    }

    // (c) Date-window convention: "tasks due last week" must provide both
    //     dueAfter and dueBefore parameters.
    [Fact]
    public void ParseModelResponse_TasksDueLastWeek_IncludesBothDateBoundaries()
    {
        const string raw = """
                           {
                             "actionName": "ListTasks",
                             "parameters": {
                               "dueAfter":  "2026-04-27",
                               "dueBefore": "2026-05-03"
                             },
                             "reason": "User asked for tasks due last week.",
                             "failureType": "None"
                           }
                           """;

        var actions = new[]
        {
            new ActionMetadata
            {
                    Name        = "ListTasks"
                  , Description = "List tasks."
                  , Parameters  = new List<ParameterMetadata>
                                  {
                                      new() { Name = "dueAfter",  IsOptional = true, AllowEmpty = true }
                                    , new() { Name = "dueBefore", IsOptional = true, AllowEmpty = true }
                                  }
            }
        };

        var result = LlmInterpreter.ParseModelResponse(raw, actions);

        Assert.Equal("ListTasks",                         result.ActionName);
        Assert.Equal(InterpreterFailureType.None,         result.FailureType);
        Assert.True(result.Parameters.ContainsKey("dueAfter")
                  , "dueAfter parameter must be present for date-window queries.");
        Assert.True(result.Parameters.ContainsKey("dueBefore")
                  , "dueBefore parameter must be present for date-window queries.");
    }

    // Catalog format: BuildActionsSummary must include parameter type names so the
    // LLM knows what value types to extract (e.g. string vs DateTimeOffset?).
    [Fact]
    public void BuildActionsSummary_IncludesParameterTypeName()
    {
        var actions = new[]
        {
            new ActionMetadata
            {
                    Name        = "ListTasks"
                  , Description = "List tasks."
                  , Parameters  = new List<ParameterMetadata>
                                  {
                                      new()
                                      {
                                              Name          = "dueAfter"
                                            , ParameterType = typeof(DateTimeOffset?)
                                            , IsOptional    = true
                                            , AllowEmpty    = true
                                            , Description   = "Filter tasks due after this date."
                                      }
                                  }
            }
        };

        var catalog = LlmInterpreter.BuildActionsSummary(actions);

        Assert.Contains("DateTimeOffset?", catalog);
        Assert.Contains("dueAfter",        catalog);
    }

    // BUG-16 specific: "add a calendar event …" must never route to BatchAddTasks.
    // The colon-style suffix ("add a calendar event for tomorrow: dentist") was
    // confusing the LLM into splitting on the colon and treating it as a batch.
    // Fix: explicit AddCalendarEvent examples added to CALENDAR ROUTING RULES.
    [Fact]
    public void ParseModelResponse_AddCalendarEventInput_ResolvesToCalendarAction_NotBatchAddTasks()
    {
        const string raw = """
                           {
                             "actionName": "AddCalendarEvent",
                             "parameters": {
                               "title":         "Dentist appointment",
                               "startDateTime": "2026-05-13T14:00:00"
                             },
                             "reason": "User asked to add a calendar event.",
                             "failureType": "None"
                           }
                           """;

        var actions = new[]
        {
            new ActionMetadata
            {
                    Name        = "AddCalendarEvent"
                  , Description = "Adds a new event to your Google Calendar."
                  , Parameters  = new List<ParameterMetadata>
                                  {
                                      new() { Name = "title",         IsOptional = false, AllowEmpty = false }
                                    , new() { Name = "startDateTime", IsOptional = false, AllowEmpty = false }
                                  }
            }
          , new ActionMetadata
            {
                    Name        = "BatchAddTasks"
                  , Description = "Adds multiple tasks at once."
                  , Parameters  = new List<ParameterMetadata>
                                  {
                                      new() { Name = "descriptions", IsOptional = false, AllowEmpty = false }
                                  }
            }
        };

        var result = LlmInterpreter.ParseModelResponse(raw, actions);

        Assert.Equal("AddCalendarEvent",              result.ActionName);
        Assert.NotEqual("BatchAddTasks",              result.ActionName);
        Assert.Equal(InterpreterFailureType.None,     result.FailureType);
        Assert.True(result.Parameters.ContainsKey("title")
                  , "title parameter must be present for AddCalendarEvent.");
        Assert.True(result.Parameters.ContainsKey("startDateTime")
                  , "startDateTime parameter must be present for AddCalendarEvent.");
    }

    // BUG-18 (journal path): "show my journals from last week" must provide both
    // fromDate and toDate parameters, not return an unfiltered list.
    [Fact]
    public void ParseModelResponse_JournalEntriesLastWeek_IncludesBothDateBoundaries()
    {
        const string raw = """
                           {
                             "actionName": "ListJournalEntries",
                             "parameters": {
                               "fromDate": "2026-05-04",
                               "toDate":   "2026-05-10"
                             },
                             "reason": "User asked for journal entries from last week.",
                             "failureType": "None"
                           }
                           """;

        var actions = new[]
        {
            new ActionMetadata
            {
                    Name        = "ListJournalEntries"
                  , Description = "Lists journal entries."
                  , Parameters  = new List<ParameterMetadata>
                                  {
                                      new() { Name = "fromDate", IsOptional = true, AllowEmpty = true }
                                    , new() { Name = "toDate",   IsOptional = true, AllowEmpty = true }
                                  }
            }
        };

        var result = LlmInterpreter.ParseModelResponse(raw, actions);

        Assert.Equal("ListJournalEntries",                result.ActionName);
        Assert.Equal(InterpreterFailureType.None,         result.FailureType);
        Assert.True(result.Parameters.ContainsKey("fromDate")
                  , "fromDate parameter must be present for journal date-window queries.");
        Assert.True(result.Parameters.ContainsKey("toDate")
                  , "toDate parameter must be present for journal date-window queries.");
    }

    // BUG-18 / BUG-16 (session state): BuildSessionStateBlock must inject computed
    // date-window boundaries so the LLM can resolve "this week", "last week", etc.
    [Fact]
    public void BuildSessionStateBlock_IncludesAllDateWindowBoundaries()
    {
        var context = new ConversationContext("test-session");

        var block = LlmInterpreter.BuildSessionStateBlock(context);

        Assert.Contains("Today:",      block);
        Assert.Contains("Yesterday:",  block);
        Assert.Contains("Tomorrow:",   block);
        Assert.Contains("This week:",  block);
        Assert.Contains("Last week:",  block);
        Assert.Contains("This month:", block);
        Assert.Contains("Last month:", block);
    }

    // BUG-16 (calendar session state): when calendar_connected = true the session
    // block must say "Calendar: connected" so the LLM knows calendar actions are available.
    [Fact]
    public void BuildSessionStateBlock_WhenCalendarConnected_IncludesCalendarConnectedLine()
    {
        var context = new ConversationContext("test-session");
        context.Metadata["calendar_connected"] = "true";

        var block = LlmInterpreter.BuildSessionStateBlock(context);

        Assert.Contains("Calendar: connected", block);
    }

    // BUG-21 additional: the ordinal word "third" must map to entryReference = "3",
    // not resolve to the wrong action or be left un-parsed.
    [Fact]
    public void ParseModelResponse_ThirdEntry_OrdinalWordMapsToPositionThree()
    {
        const string raw = """
                           {
                             "actionName": "GetJournalEntry",
                             "parameters": { "entryReference": "3" },
                             "reason": "User said 'show the third entry'; 'third' maps to position 3.",
                             "failureType": "None"
                           }
                           """;

        var actions = new[]
        {
            new ActionMetadata
            {
                    Name        = "GetJournalEntry"
                  , Description = "Retrieves a journal entry by position."
                  , Parameters  = new List<ParameterMetadata>
                                  {
                                      new() { Name = "entryReference", IsOptional = false, AllowEmpty = false }
                                  }
            }
          , new ActionMetadata
            {
                    Name        = "ListJournalEntries"
                  , Description = "Lists journal entries."
                  , Parameters  = new List<ParameterMetadata>()
            }
        };

        var result = LlmInterpreter.ParseModelResponse(raw, actions);

        Assert.Equal("GetJournalEntry",               result.ActionName);
        Assert.Equal("3",                             result.Parameters["entryReference"]);
        Assert.Equal(InterpreterFailureType.None,     result.FailureType);
    }

    [Fact]
    public async Task InterpretWithContext_WhenModelIsNotUsable_WithHttp429Reason_IncludesRateLimitDetail()
    {
        // Arrange: model is in catalog but flagged unusable with a rate-limit reason
        const string failureReason = "HTTP 429: too many requests — daily quota exhausted";

        var catalog = new LlmModelCatalog();
        catalog.Add(new LlmModelInfo( Name             : "gemini-2.5-flash"
                                    , IsUsable          : false
                                    , FailureReason     : failureReason
                                    , SupportsChat      : true
                                    , SupportsStreaming  : true ));

        var settings = new LlmClientSettings
                       {
                               Provider     = LlmProvider.Gemini
                             , DefaultModel = "gemini-2.5-flash"
                       };

        var interpreter = BuildInterpreter(catalog, settings);
        var context     = new ConversationContext("test-session");
        context.Metadata["model"] = "gemini-2.5-flash";

        // Act
        var result = await interpreter.InterpretWithContext("what time is it", context);

        // Assert: the HTTP 429 detail is surfaced in the reason so the caller can
        // distinguish a rate-limit failure from a missing-model failure
        Assert.Equal(InterpreterFailureType.NoMatchingAction, result.FailureType);
        Assert.Contains("HTTP 429:", result.Reason);
    }

}
