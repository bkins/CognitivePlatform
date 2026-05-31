using Moq;
using CognitivePlatform.Api.Domains.DailyRecord;
using CognitivePlatform.Api.Interpreter;
using CognitivePlatform.Api.Models;
using CognitivePlatform.Api.Registry;

namespace CognitivePlatform.Tests;

public class FastPathResolverTests
{
    private readonly Mock<IActionRegistry>         _registryMock    = new();
    private readonly Mock<IDailyRecordCommandParser> _dailyParserMock = new();
    private readonly FastPathResolver              _resolver;

    private static ActionMetadata MakeAction(string name)
        => new() { Name = name, Parameters = new List<ParameterMetadata>() };

    public FastPathResolverTests()
    {
        var actions = new List<ActionMetadata>
        {
              MakeAction("ListActions")
            , MakeAction("AddJournalEntry")
            , MakeAction("JournalEntriesOnThisDay")
            , MakeAction("ListJournalEntries")
            , MakeAction("AddTask")
            , MakeAction("ListTasks")
            , MakeAction("CompleteTask")
            , MakeAction("CompleteBatch")
            , MakeAction("DeleteTask")
            , MakeAction("AnalyzeTasks")
            , MakeAction("UpdateTaskPriority")
            , MakeAction("UpdateTaskDueDate")
            , MakeAction("SetModel")
            , MakeAction("SetProvider")
            , MakeAction("ListModels")
            , MakeAction("ListProviders")
            , MakeAction("OpenDay")
            , MakeAction("AddCheckpoint")
            , MakeAction("CloseDay")
            , MakeAction("ClaimRolledOverTasks")
            , MakeAction("ListPersonalities")
            , MakeAction("GetActivePersonality")
            , MakeAction("SetPersonality")
            , MakeAction("ListPersonas")
            , MakeAction("GetActivePersona")
            , MakeAction("SetPersona")
            , MakeAction("GetStepCount")
            , MakeAction("GetSleepSummary")
            , MakeAction("GetHeartRate")
            , MakeAction("GetDistance")
        };

        _registryMock.Setup(registry => registry.Actions).Returns(actions);
        _registryMock.Setup(registry => registry.FastPathActions).Returns(new List<ActionMetadata>());

        _resolver = new FastPathResolver(_registryMock.Object, _dailyParserMock.Object);
    }

    // ================================================================
    // MODE 0: CAPABILITIES QUERY
    // ================================================================

    [Fact]
    public void TryResolve_ResolvesToListActions_ForWhatCanYouDoPhrase()
    {
        var resolved = _resolver.TryResolve("what can you do", out var action, out var parameters);

        Assert.True(resolved);
        Assert.Equal("ListActions", action!.Name);
        Assert.NotNull(parameters);
    }

    [Fact]
    public void TryResolve_ResolvesToListActions_ForHelpPhrase()
    {
        var resolved = _resolver.TryResolve("help", out var action, out _);

        Assert.True(resolved);
        Assert.Equal("ListActions", action!.Name);
    }

    [Fact]
    public void TryResolve_ResolvesToListActions_ForListCommandsPhrase()
    {
        var resolved = _resolver.TryResolve("list commands", out var action, out _);

        Assert.True(resolved);
        Assert.Equal("ListActions", action!.Name);
    }

    // ================================================================
    // MODE 1.1: COLON PREFIX
    // ================================================================

    [Fact]
    public void TryResolve_ResolvesToAddJournalEntry_ForJournalColonPrefix()
    {
        var resolved = _resolver.TryResolve("journal: Had a good day", out var action, out var parameters);

        Assert.True(resolved);
        Assert.Equal("AddJournalEntry", action!.Name);
        Assert.Equal("Had a good day",  parameters!["text"]);
    }

    [Fact]
    public void TryResolve_ResolvesToAddTask_ForTaskColonPrefix()
    {
        var resolved = _resolver.TryResolve("task: Buy milk", out var action, out var parameters);

        Assert.True(resolved);
        Assert.Equal("AddTask",    action!.Name);
        Assert.Equal("Buy milk",   parameters!["shortDescription"]);
    }

    // ================================================================
    // MODE 1.2: SLASH PREFIX
    // ================================================================

    [Fact]
    public void TryResolve_ResolvesToAddJournalEntry_ForSlashJournalAdd()
    {
        var resolved = _resolver.TryResolve("/journal add Had a good day", out var action, out var parameters);

        Assert.True(resolved);
        Assert.Equal("AddJournalEntry", action!.Name);
        Assert.Equal("Had a good day",  parameters!["text"]);
    }

    [Fact]
    public void TryResolve_ResolvesToListTasks_WithIncludeCompleted_ForSlashTaskListNoQualifier()
    {
        var resolved = _resolver.TryResolve("/task list", out var action, out var parameters);

        Assert.True(resolved);
        Assert.Equal("ListTasks", action!.Name);
        Assert.Equal("true",      parameters!["includeCompleted"]);
    }

    [Fact]
    public void TryResolve_ResolvesToListTasks_WithOnlyUrgent_ForSlashTaskListUrgent()
    {
        var resolved = _resolver.TryResolve("/task list urgent", out var action, out var parameters);

        Assert.True(resolved);
        Assert.Equal("ListTasks", action!.Name);
        Assert.Equal("true",      parameters!["onlyUrgent"]);
    }

    [Fact]
    public void TryResolve_ResolvesToListTasks_WithOnlyImportant_ForSlashTaskListImportant()
    {
        var resolved = _resolver.TryResolve("/task list important", out var action, out var parameters);

        Assert.True(resolved);
        Assert.Equal("ListTasks", action!.Name);
        Assert.Equal("true",      parameters!["onlyImportant"]);
    }

    [Fact]
    public void TryResolve_ResolvesToCompleteTask_WithTaskReference_ForSlashTaskComplete()
    {
        var resolved = _resolver.TryResolve("/task complete 1", out var action, out var parameters);

        Assert.True(resolved);
        Assert.Equal("CompleteTask", action!.Name);
        Assert.Equal("1",            parameters!["taskReference"]);
    }

    [Fact]
    public void TryResolve_ResolvesToDeleteTask_ForSlashTaskDelete()
    {
        var resolved = _resolver.TryResolve("/task delete 7", out var action, out var parameters);

        Assert.True(resolved);
        Assert.Equal("DeleteTask", action!.Name);
        Assert.Equal("7",          parameters!["taskReference"]);
    }

    [Fact]
    public void TryResolve_ResolvesToAnalyzeTasks_ForSlashTaskAnalyze()
    {
        var resolved = _resolver.TryResolve("/task analyze", out var action, out _);

        Assert.True(resolved);
        Assert.Equal("AnalyzeTasks", action!.Name);
    }

    [Fact]
    public void TryResolve_ResolvesToUpdateTaskDueDate_ForSlashTaskDue()
    {
        var resolved = _resolver.TryResolve("/task due 2 Friday", out var action, out var parameters);

        Assert.True(resolved);
        Assert.Equal("UpdateTaskDueDate", action!.Name);
        Assert.Equal("2",                 parameters!["taskReference"]);
        Assert.Equal("Friday",            parameters["dueDateText"]);
    }

    // ================================================================
    // MODE 2: TASK-SPECIFIC FAST PATHS
    // ================================================================

    [Fact]
    public void TryResolve_ResolvesToAnalyzeTasks_ForPrioritizeMyTasksPhrase()
    {
        var resolved = _resolver.TryResolve("prioritize my tasks", out var action, out _);

        Assert.True(resolved);
        Assert.Equal("AnalyzeTasks", action!.Name);
    }

    [Fact]
    public void TryResolve_ResolvesToAnalyzeTasks_ForTaskMatrixPhrase()
    {
        var resolved = _resolver.TryResolve("show my task matrix", out var action, out _);

        Assert.True(resolved);
        Assert.Equal("AnalyzeTasks", action!.Name);
    }

    [Fact]
    public void TryResolve_ResolvesToListTasks_WithIncludeCompleted_ForShowMyTasksPhrase()
    {
        var resolved = _resolver.TryResolve("show my tasks", out var action, out var parameters);

        Assert.True(resolved);
        Assert.Equal("ListTasks", action!.Name);
        Assert.Equal("true",      parameters!["includeCompleted"]);
    }

    [Fact]
    public void TryResolve_ResolvesToListTasks_WithOnlyUrgent_ForShowUrgentTasks()
    {
        var resolved = _resolver.TryResolve("show urgent tasks", out var action, out var parameters);

        Assert.True(resolved);
        Assert.Equal("ListTasks", action!.Name);
        Assert.Equal("true",      parameters!["onlyUrgent"]);
    }

    [Fact]
    public void TryResolve_ResolvesToListTasks_WithOnlyImportant_ForShowImportantTasks()
    {
        var resolved = _resolver.TryResolve("show important tasks", out var action, out var parameters);

        Assert.True(resolved);
        Assert.Equal("ListTasks", action!.Name);
        Assert.Equal("true",      parameters!["onlyImportant"]);
    }

    [Fact]
    public void TryResolve_ResolvesToCompleteBatch_WithBothRefs_ForMultipleTasksAreDone()
    {
        var resolved = _resolver.TryResolve("tasks 2 and 4 are done", out var action, out var parameters);

        Assert.True(resolved);
        Assert.Equal("CompleteBatch", action!.Name);
        Assert.Equal("2|4",           parameters!["taskReferences"]);
    }

    [Fact]
    public void TryResolve_ResolvesToCompleteTask_ForCompleteTaskPhrase()
    {
        var resolved = _resolver.TryResolve("complete task 3", out var action, out var parameters);

        Assert.True(resolved);
        Assert.Equal("CompleteTask", action!.Name);
        Assert.Equal("3",            parameters!["taskReference"]);
    }

    [Fact]
    public void TryResolve_ResolvesToDeleteTask_ForDeleteTaskPhrase()
    {
        var resolved = _resolver.TryResolve("delete task 5", out var action, out var parameters);

        Assert.True(resolved);
        Assert.Equal("DeleteTask", action!.Name);
        Assert.Equal("5",          parameters!["taskReference"]);
    }

    [Fact]
    public void TryResolve_ResolvesToUpdateTaskPriority_WithPriorityHigh_ForHighPriorityPhrase()
    {
        var resolved = _resolver.TryResolve("make task 2 high priority", out var action, out var parameters);

        Assert.True(resolved);
        Assert.Equal("UpdateTaskPriority", action!.Name);
        Assert.Equal("2",                  parameters!["taskReference"]);
        Assert.Equal("High",               parameters["priority"]);
    }

    [Fact]
    public void TryResolve_ResolvesToUpdateTaskDueDate_ForIsDuePhrase()
    {
        var resolved = _resolver.TryResolve("task 3 is due friday", out var action, out var parameters);

        Assert.True(resolved);
        Assert.Equal("UpdateTaskDueDate", action!.Name);
        Assert.Equal("3",                 parameters!["taskReference"]);
        Assert.Equal("friday",            parameters["dueDateText"]);
    }

    [Fact]
    public void TryResolve_ResolvesToUpdateTaskDueDate_WithEmptyDateText_ForClearDueDatePhrase()
    {
        var resolved = _resolver.TryResolve("clear due date on task 4", out var action, out var parameters);

        Assert.True(resolved);
        Assert.Equal("UpdateTaskDueDate", action!.Name);
        Assert.Equal("4",                 parameters!["taskReference"]);
        Assert.Equal(string.Empty,        parameters["dueDateText"]);
    }

    // ================================================================
    // MODE 3: GENERIC FAST PATH
    // ================================================================

    [Fact]
    public void TryResolve_ResolvesViaFastPathAction_WhenSignalMatchesAndActionConfigured()
    {
        var fastPathAction = new ActionMetadata
                             {
                                     Name = "AddQuickNote"
                                   , Parameters = new List<ParameterMetadata>
                                                  {
                                                          new()
                                                          {
                                                                  Name = "text"
                                                                , IsOptional = false
                                                          }
                                                  }
                             };
        _registryMock.Setup(reg => reg.FastPathActions)
                     .Returns(new List<ActionMetadata> { fastPathAction });

        var resolved = _resolver.TryResolve("note that pick up the kids", out var action, out var parameters);

        Assert.True(resolved);
        Assert.Equal("AddQuickNote",     action!.Name);
        Assert.Equal("pick up the kids", parameters!["text"]);
    }

    // ================================================================
    // NEGATIVE CASES
    // ================================================================

    [Fact]
    public void TryResolve_ReturnsFalse_ForUnrecognisedInput()
    {
        var resolved = _resolver.TryResolve("today the weather is nice", out var action, out var parameters);

        Assert.False(resolved);
        Assert.Null(action);
        Assert.Null(parameters);
    }

    [Fact]
    public void TryResolve_ReturnsFalse_ForEmptyInput()
    {
        var resolved = _resolver.TryResolve(string.Empty, out var action, out _);

        Assert.False(resolved);
        Assert.Null(action);
    }

    // ================================================================
    // MODE 1.1b: MULTILINE JOURNAL BLOCK
    // ================================================================

    [Fact]
    public void TryResolve_ResolvesToAddJournalEntry_ForMultilineJournalBlock()
    {
        var input = "Journal\nToday I felt focused and productive.\nTags: \"work\", \"deep-thought\"\nMood: \"Calm\"\nMoodScore: 4";

        var resolved = _resolver.TryResolve(input, out var action, out var parameters);

        Assert.True(resolved);
        Assert.Equal("AddJournalEntry",                      action!.Name);
        Assert.Equal("Today I felt focused and productive.", parameters!["text"]);
        Assert.Equal("work,deep-thought",                    parameters["tags"]);
        Assert.Equal("Calm",                                 parameters["mood"]);
        Assert.Equal("4",                                    parameters["moodScore"]);
    }

    [Fact]
    public void TryResolve_ResolvesToAddJournalEntry_ForMultilineJournalBlock_TextOnly()
    {
        var input = "Journal\nShort entry with no metadata.";

        var resolved = _resolver.TryResolve(input, out var action, out var parameters);

        Assert.True(resolved);
        Assert.Equal("AddJournalEntry",                action!.Name);
        Assert.Equal("Short entry with no metadata.",  parameters!["text"]);
    }

    [Fact]
    public void TryResolve_ReturnsFalse_ForMultilineJournalBlock_MissingTextLine()
    {
        var input = "Journal\nTags: \"work\"\nMood: \"Calm\"";

        var resolved = _resolver.TryResolve(input, out _, out _);

        Assert.False(resolved);
    }

    // ================================================================
    // DESTRUCTIVE VERB GUARD (generic fast path)
    // ================================================================

    [Fact]
    public void TryResolve_ReturnsFalse_WhenInputStartsWithDeleteVerb()
    {
        var resolved = _resolver.TryResolve("delete the journal entry abc123", out _, out _);

        Assert.False(resolved);
    }

    [Fact]
    public void TryResolve_ReturnsFalse_WhenInputStartsWithRemoveVerb()
    {
        var resolved = _resolver.TryResolve("remove the note I made yesterday", out _, out _);

        Assert.False(resolved);
    }

    // ================================================================
    // LLM META COMMANDS
    // ================================================================

    [Theory]
    [InlineData("set model to gemini-2.5-pro",           "gemini-2.5-pro")]
    [InlineData("use model llama-3.3-70b-versatile",     "llama-3.3-70b-versatile")]
    [InlineData("switch model to gpt-4o-mini",           "gpt-4o-mini")]
    [InlineData("change model to gemini-2.5-flash-lite", "gemini-2.5-flash-lite")]
    [InlineData("switch to model gemini-2.5-pro",        "gemini-2.5-pro")]
    public void TryResolve_ResolvesToSetModel_WithExtractedModelName(string input, string expectedModel)
    {
        var resolved = _resolver.TryResolve(input, out var action, out var parameters);

        Assert.True(resolved);
        Assert.Equal("SetModel", action!.Name);
        Assert.True(parameters!.TryGetValue("model", out var actualModel));
        Assert.Equal(expectedModel, actualModel);
    }

    [Theory]
    [InlineData("set provider to Groq",    "Groq")]
    [InlineData("use provider OpenRouter", "OpenRouter")]
    [InlineData("switch provider to Groq", "Groq")]
    [InlineData("switch to provider Groq", "Groq")]
    public void TryResolve_ResolvesToSetProvider_WithExtractedProviderName(string input, string expectedProvider)
    {
        var resolved = _resolver.TryResolve(input, out var action, out var parameters);

        Assert.True(resolved);
        Assert.Equal("SetProvider", action!.Name);
        Assert.True(parameters!.TryGetValue("provider", out var actualProvider));
        Assert.Equal(expectedProvider, actualProvider);
    }

    [Theory]
    [InlineData("switch to Groq")]
    [InlineData("use Groq")]
    [InlineData("use Gemini")]
    public void TryResolve_ResolvesToSetProvider_ForBareKnownProviderName(string input)
    {
        var resolved = _resolver.TryResolve(input, out var action, out _);

        Assert.True(resolved);
        Assert.Equal("SetProvider", action!.Name);
    }

    [Theory]
    [InlineData("list models")]
    [InlineData("show models")]
    [InlineData("what models are available")]
    public void TryResolve_ResolvesToListModels(string input)
    {
        var resolved = _resolver.TryResolve(input, out var action, out _);

        Assert.True(resolved);
        Assert.Equal("ListModels", action!.Name);
    }

    [Theory]
    [InlineData("list providers")]
    [InlineData("show providers")]
    [InlineData("what providers are available")]
    public void TryResolve_ResolvesToListProviders(string input)
    {
        var resolved = _resolver.TryResolve(input, out var action, out _);

        Assert.True(resolved);
        Assert.Equal("ListProviders", action!.Name);
    }

    [Theory]
    [InlineData("show me my calendar")]
    [InlineData("what time is it")]
    [InlineData("switch to dark mode")]
    public void TryResolve_DoesNotMatch_ForNonMetaInput(string input)
    {
        // None of the normal inputs should accidentally resolve via the LLM meta path
        _resolver.TryResolve(input, out var action, out _);

        // We only care that it didn't match SetModel / SetProvider / ListModels / ListProviders
        Assert.True(action is null
                 || (   action.Name != "SetModel"
                     && action.Name != "SetProvider"
                     && action.Name != "ListModels"
                     && action.Name != "ListProviders"));
    }

    // ================================================================
    // MODE 0.5: DAILY RECORD — implicit tasks via bare "Plan:\n" (BUG-31)
    // ================================================================

    [Fact]
    public void TryResolve_Plan_WithImplicitTasks_SetsTasksParameter()
    {
        _dailyParserMock
            .Setup(parser => parser.Parse(It.IsAny<string>()))
            .Returns(new ParsedDailyCommand
                     {
                             CommandType = DailyCommandType.Plan
                           , BodyText    = string.Empty
                           , Tasks       = new List<string> { "Finish the sprint report by tomorrow" }
                     });

        var resolved = _resolver.TryResolve( "Plan:\nFinish the sprint report by tomorrow"
                                           , out var action
                                           , out var parameters );

        Assert.True(resolved);
        Assert.Equal("OpenDay",                                action!.Name);
        Assert.Equal("Finish the sprint report by tomorrow",   parameters!["tasks"]);
    }

    [Fact]
    public void TryResolve_Plan_WithImplicitTasks_SetsNonEmptyOpeningText()
    {
        _dailyParserMock
            .Setup(parser => parser.Parse(It.IsAny<string>()))
            .Returns(new ParsedDailyCommand
                     {
                             CommandType = DailyCommandType.Plan
                           , BodyText    = string.Empty
                           , Tasks       = new List<string> { "Write the report" }
                     });

        var resolved = _resolver.TryResolve( "Plan:\nWrite the report"
                                           , out _
                                           , out var parameters );

        Assert.True(resolved);
        Assert.False(string.IsNullOrWhiteSpace(parameters!["openingText"])
                   , "openingText must not be empty when Plan: body is absent");
    }

    [Fact]
    public void TryResolve_Plan_WithMultipleImplicitTasks_SetsCommaSeparatedTasksParameter()
    {
        _dailyParserMock
            .Setup(parser => parser.Parse(It.IsAny<string>()))
            .Returns(new ParsedDailyCommand
                     {
                             CommandType = DailyCommandType.Plan
                           , BodyText    = string.Empty
                           , Tasks       = new List<string> { "Task one", "Task two", "Task three" }
                     });

        var resolved = _resolver.TryResolve( "Plan:\nTask one\nTask two\nTask three"
                                           , out _
                                           , out var parameters );

        Assert.True(resolved);
        Assert.Equal("Task one, Task two, Task three", parameters!["tasks"]);
    }

    [Fact]
    public void TryResolve_Plan_WithBodyText_PassesBodyTextAsOpeningText()
    {
        _dailyParserMock
            .Setup(parser => parser.Parse(It.IsAny<string>()))
            .Returns(new ParsedDailyCommand
                     {
                             CommandType = DailyCommandType.Plan
                           , BodyText    = "Focused day on the API refactor."
                           , Tasks       = new List<string>()
                     });

        var resolved = _resolver.TryResolve( "Plan: Focused day on the API refactor."
                                           , out _
                                           , out var parameters );

        Assert.True(resolved);
        Assert.Equal("Focused day on the API refactor.", parameters!["openingText"]);
        Assert.False(parameters.ContainsKey("tasks"));
    }

    // ================================================================
    // MODE 2.5: PERSONALITY FAST PATHS
    // ================================================================

    [Fact]
    public void TryResolve_ResolvesToListPersonalities_ForListPhrase()
    {
        var resolved = _resolver.TryResolve("list all personalities", out var action, out _);

        Assert.True(resolved);
        Assert.Equal("ListPersonalities", action!.Name);
    }

    [Fact]
    public void TryResolve_ResolvesToListPersonalities_ForShowPhrase()
    {
        var resolved = _resolver.TryResolve("show available personalities", out var action, out _);

        Assert.True(resolved);
        Assert.Equal("ListPersonalities", action!.Name);
    }

    [Fact]
    public void TryResolve_ResolvesToListPersonalities_ForWhatPersonalitiesPhrase()
    {
        var resolved = _resolver.TryResolve("what personalities are available?", out var action, out _);

        Assert.True(resolved);
        Assert.Equal("ListPersonalities", action!.Name);
    }

    [Fact]
    public void TryResolve_ResolvesToGetActivePersonality_ForActivePhrase()
    {
        var resolved = _resolver.TryResolve("what personality is active?", out var action, out _);

        Assert.True(resolved);
        Assert.Equal("GetActivePersonality", action!.Name);
    }

    [Fact]
    public void TryResolve_ResolvesToGetActivePersonality_ForCurrentPhrase()
    {
        var resolved = _resolver.TryResolve("which personality am I using?", out var action, out _);

        Assert.True(resolved);
        Assert.Equal("GetActivePersonality", action!.Name);
    }

    [Fact]
    public void TryResolve_ResolvesToSetPersonality_ForSwitchToPhrase_AndExtractsName()
    {
        var resolved = _resolver.TryResolve("switch to the Zen personality", out var action, out var parameters);

        Assert.True(resolved);
        Assert.Equal("SetPersonality", action!.Name);
        Assert.Equal("zen", parameters!["name"]);
    }

    [Fact]
    public void TryResolve_ResolvesToSetPersonality_ForUseThePhrase_AndExtractsName()
    {
        var resolved = _resolver.TryResolve("use the Programmer personality", out var action, out var parameters);

        Assert.True(resolved);
        Assert.Equal("SetPersonality", action!.Name);
        Assert.Equal("programmer", parameters!["name"]);
    }

    [Fact]
    public void TryResolve_ResolvesToSetPersonality_ForSetPersonalityToPhrase_AndExtractsName()
    {
        var resolved = _resolver.TryResolve("set personality to Witty", out var action, out var parameters);

        Assert.True(resolved);
        Assert.Equal("SetPersonality", action!.Name);
        Assert.Equal("witty", parameters!["name"]);
    }

    [Fact]
    public void TryResolve_ResolvesToSetPersonality_ForActivatePhrase_AndExtractsName()
    {
        var resolved = _resolver.TryResolve("activate Motivational Coach", out var action, out var parameters);

        Assert.True(resolved);
        Assert.Equal("SetPersonality", action!.Name);
        Assert.Equal("motivational coach", parameters!["name"]);
    }

    [Fact]
    public void TryResolve_ResolvesToListPersonalities_ForPrefixCommand()
    {
        var resolved = _resolver.TryResolve("/personality list", out var action, out _);

        Assert.True(resolved);
        Assert.Equal("ListPersonalities", action!.Name);
    }

    [Fact]
    public void TryResolve_ResolvesToGetActivePersonality_ForPrefixCommand()
    {
        var resolved = _resolver.TryResolve("/personality active", out var action, out _);

        Assert.True(resolved);
        Assert.Equal("GetActivePersonality", action!.Name);
    }

    [Fact]
    public void TryResolve_ResolvesToSetPersonality_ForPrefixSetCommand()
    {
        var resolved = _resolver.TryResolve("/personality set Zen", out var action, out var parameters);

        Assert.True(resolved);
        Assert.Equal("SetPersonality", action!.Name);
        Assert.Equal("Zen",            parameters!["name"]);
    }

    // ================================================================
    // MODE 2.6: PERSONA FAST PATHS (alias vocabulary)
    // ================================================================

    [Fact]
    public void TryResolve_ResolvesToListPersonas_ForListPhrase()
    {
        var resolved = _resolver.TryResolve("list all personas", out var action, out _);

        Assert.True(resolved);
        Assert.Equal("ListPersonas", action!.Name);
    }

    [Fact]
    public void TryResolve_ResolvesToListPersonas_ForShowPhrase()
    {
        var resolved = _resolver.TryResolve("show available personas", out var action, out _);

        Assert.True(resolved);
        Assert.Equal("ListPersonas", action!.Name);
    }

    [Fact]
    public void TryResolve_ResolvesToListPersonas_ForWhatPersonasPhrase()
    {
        var resolved = _resolver.TryResolve("what personas are available?", out var action, out _);

        Assert.True(resolved);
        Assert.Equal("ListPersonas", action!.Name);
    }

    [Fact]
    public void TryResolve_ResolvesToGetActivePersona_ForActivePhrase()
    {
        var resolved = _resolver.TryResolve("what persona is active?", out var action, out _);

        Assert.True(resolved);
        Assert.Equal("GetActivePersona", action!.Name);
    }

    [Fact]
    public void TryResolve_ResolvesToGetActivePersona_ForCurrentPersonaPhrase()
    {
        var resolved = _resolver.TryResolve("which persona am I using?", out var action, out _);

        Assert.True(resolved);
        Assert.Equal("GetActivePersona", action!.Name);
    }

    [Fact]
    public void TryResolve_ResolvesToSetPersona_ForSetPersonaToPhrase_AndExtractsName()
    {
        var resolved = _resolver.TryResolve("set persona to Zen", out var action, out var parameters);

        Assert.True(resolved);
        Assert.Equal("SetPersona", action!.Name);
        Assert.Equal("zen",        parameters!["name"]);
    }

    [Fact]
    public void TryResolve_ResolvesToSetPersona_ForUsePersonaPhrase_AndExtractsName()
    {
        var resolved = _resolver.TryResolve("use persona Programmer", out var action, out var parameters);

        Assert.True(resolved);
        Assert.Equal("SetPersona",  action!.Name);
        Assert.Equal("programmer",  parameters!["name"]);
    }

    [Fact]
    public void TryResolve_ResolvesToSetPersona_ForChangePersonaToPhrase_AndExtractsName()
    {
        var resolved = _resolver.TryResolve("change persona to Witty", out var action, out var parameters);

        Assert.True(resolved);
        Assert.Equal("SetPersona", action!.Name);
        Assert.Equal("witty",      parameters!["name"]);
    }

    [Fact]
    public void TryResolve_ResolvesToSetPersona_ForSetMyPersonaToPhrase_AndExtractsName()
    {
        var resolved = _resolver.TryResolve("set my persona to Tech Expert", out var action, out var parameters);

        Assert.True(resolved);
        Assert.Equal("SetPersona", action!.Name);
        Assert.Equal("tech expert", parameters!["name"]);
    }

    [Fact]
    public void TryResolve_ResolvesToListPersonas_ForPrefixCommand()
    {
        var resolved = _resolver.TryResolve("/persona list", out var action, out _);

        Assert.True(resolved);
        Assert.Equal("ListPersonas", action!.Name);
    }

    [Fact]
    public void TryResolve_ResolvesToGetActivePersona_ForPrefixCommand()
    {
        var resolved = _resolver.TryResolve("/persona active", out var action, out _);

        Assert.True(resolved);
        Assert.Equal("GetActivePersona", action!.Name);
    }

    [Fact]
    public void TryResolve_ResolvesToSetPersona_ForPrefixSetCommand()
    {
        var resolved = _resolver.TryResolve("/persona set Zen", out var action, out var parameters);

        Assert.True(resolved);
        Assert.Equal("SetPersona", action!.Name);
        Assert.Equal("Zen",        parameters!["name"]);
    }

    [Fact]
    public void TryResolve_ResolvesToSetPersonality_ForSwitchToTheZenPersonaPhrase()
    {
        var resolved = _resolver.TryResolve("switch to the Zen persona", out var action, out var parameters);

        Assert.True(resolved);
        Assert.Equal("SetPersonality", action!.Name);
        Assert.Equal("zen",            parameters!["name"]);
    }

    // ================================================================
    // MODE 2.8: HEALTH FAST PATHS
    // ================================================================

    [Fact]
    public void TryResolve_ResolvesToGetStepCount_ForStepsTodayPhrase()
    {
        var resolved = _resolver.TryResolve("steps today", out var action, out var parameters);

        Assert.True(resolved);
        Assert.Equal("GetStepCount", action!.Name);
        Assert.Equal("today",        parameters!["dateRange"]);
    }

    [Fact]
    public void TryResolve_ResolvesToGetStepCount_ForHowManyStepsPhrase()
    {
        var resolved = _resolver.TryResolve("how many steps did I walk yesterday?", out var action, out var parameters);

        Assert.True(resolved);
        Assert.Equal("GetStepCount", action!.Name);
        Assert.Equal("yesterday",    parameters!["dateRange"]);
    }

    [Fact]
    public void TryResolve_ResolvesToGetStepCount_DefaultsToToday_WhenNoDateInPhrase()
    {
        var resolved = _resolver.TryResolve("step count", out var action, out var parameters);

        Assert.True(resolved);
        Assert.Equal("GetStepCount", action!.Name);
        Assert.Equal("today",        parameters!["dateRange"]);
    }

    [Fact]
    public void TryResolve_ResolvesToGetStepCount_ForLastWeekPhrase()
    {
        var resolved = _resolver.TryResolve("steps last week", out var action, out var parameters);

        Assert.True(resolved);
        Assert.Equal("GetStepCount", action!.Name);
        Assert.Equal("last week",    parameters!["dateRange"]);
    }

    [Fact]
    public void TryResolve_ResolvesToGetSleepSummary_ForSleepLastNightPhrase()
    {
        var resolved = _resolver.TryResolve("sleep last night", out var action, out var parameters);

        Assert.True(resolved);
        Assert.Equal("GetSleepSummary", action!.Name);
        Assert.Equal("yesterday",       parameters!["dateRange"]);
    }

    [Fact]
    public void TryResolve_ResolvesToGetSleepSummary_ForHowWasMySleepPhrase()
    {
        var resolved = _resolver.TryResolve("how was my sleep last night?", out var action, out var parameters);

        Assert.True(resolved);
        Assert.Equal("GetSleepSummary", action!.Name);
        Assert.Equal("yesterday",       parameters!["dateRange"]);
    }

    [Fact]
    public void TryResolve_ResolvesToGetSleepSummary_DefaultsToYesterday_WhenNoDateInPhrase()
    {
        var resolved = _resolver.TryResolve("sleep summary", out var action, out var parameters);

        Assert.True(resolved);
        Assert.Equal("GetSleepSummary", action!.Name);
        Assert.Equal("yesterday",       parameters!["dateRange"]);
    }

    [Fact]
    public void TryResolve_ResolvesToGetHeartRate_ForHeartRateTodayPhrase()
    {
        var resolved = _resolver.TryResolve("heart rate today", out var action, out var parameters);

        Assert.True(resolved);
        Assert.Equal("GetHeartRate", action!.Name);
        Assert.Equal("today",        parameters!["dateRange"]);
    }

    [Fact]
    public void TryResolve_ResolvesToGetHeartRate_ForRestingHeartRatePhrase()
    {
        var resolved = _resolver.TryResolve("resting heart rate", out var action, out var parameters);

        Assert.True(resolved);
        Assert.Equal("GetHeartRate", action!.Name);
        Assert.Equal("today",        parameters!["dateRange"]);
    }

    [Fact]
    public void TryResolve_ResolvesToGetHeartRate_ForAverageBpmPhrase()
    {
        var resolved = _resolver.TryResolve("average bpm yesterday", out var action, out var parameters);

        Assert.True(resolved);
        Assert.Equal("GetHeartRate", action!.Name);
        Assert.Equal("yesterday",    parameters!["dateRange"]);
    }

    [Fact]
    public void TryResolve_ResolvesToGetDistance_ForDistanceTodayPhrase()
    {
        var resolved = _resolver.TryResolve("distance today", out var action, out var parameters);

        Assert.True(resolved);
        Assert.Equal("GetDistance", action!.Name);
        Assert.Equal("today",       parameters!["dateRange"]);
    }

    [Fact]
    public void TryResolve_ResolvesToGetDistance_ForHowFarDidIWalkPhrase()
    {
        var resolved = _resolver.TryResolve("how far did I walk yesterday?", out var action, out var parameters);

        Assert.True(resolved);
        Assert.Equal("GetDistance", action!.Name);
        Assert.Equal("yesterday",   parameters!["dateRange"]);
    }

    [Fact]
    public void TryResolve_ResolvesToGetDistance_ForDistanceLastWeekPhrase()
    {
        var resolved = _resolver.TryResolve("distance last week", out var action, out var parameters);

        Assert.True(resolved);
        Assert.Equal("GetDistance", action!.Name);
        Assert.Equal("last week",   parameters!["dateRange"]);
    }

    // ================================================================
    // BUG-28: CloseDay fast-path synonyms
    // "close day", "end of day", "day done", "eod" (no colon) must all
    // resolve to CloseDay without requiring the LLM.
    // ================================================================

    [Fact]
    public void TryResolve_ResolvesToCloseDay_ForCloseDayPhrase()
    {
        var resolved = _resolver.TryResolve("close day", out var action, out var parameters);

        Assert.True(resolved);
        Assert.Equal("CloseDay", action!.Name);
        Assert.NotNull(parameters);
        Assert.False(parameters!.ContainsKey("closingText")
                   , "closingText must not be set when none follows the phrase");
    }

    [Fact]
    public void TryResolve_ResolvesToCloseDay_ForCloseDayWithPeriod()
    {
        var resolved = _resolver.TryResolve("Close day.", out var action, out _);

        Assert.True(resolved);
        Assert.Equal("CloseDay", action!.Name);
    }

    [Fact]
    public void TryResolve_ResolvesToCloseDay_ForEndOfDayPhrase()
    {
        var resolved = _resolver.TryResolve("End of day.", out var action, out var parameters);

        Assert.True(resolved);
        Assert.Equal("CloseDay", action!.Name);
        Assert.False(parameters!.ContainsKey("closingText"));
    }

    [Fact]
    public void TryResolve_ResolvesToCloseDay_ForDayDonePhrase()
    {
        var resolved = _resolver.TryResolve("Day done", out var action, out _);

        Assert.True(resolved);
        Assert.Equal("CloseDay", action!.Name);
    }

    [Fact]
    public void TryResolve_ResolvesToCloseDay_ForEodWithoutColon()
    {
        var resolved = _resolver.TryResolve("eod", out var action, out _);

        Assert.True(resolved);
        Assert.Equal("CloseDay", action!.Name);
    }

    [Fact]
    public void TryResolve_ResolvesToCloseDay_ExtractsClosingText_WhenTextFollowsPhrase()
    {
        var resolved = _resolver.TryResolve("close day Good work today.", out var action, out var parameters);

        Assert.True(resolved);
        Assert.Equal("CloseDay",          action!.Name);
        Assert.Equal("Good work today.", parameters!["closingText"]);
    }

    [Fact]
    public void TryResolve_ResolvesToCloseDay_ExtractsClosingText_AfterEndOfDayPeriod()
    {
        var resolved = _resolver.TryResolve("End of day. Solid session.", out var action, out var parameters);

        Assert.True(resolved);
        Assert.Equal("CloseDay",       action!.Name);
        Assert.Equal("Solid session.", parameters!["closingText"]);
    }
}
