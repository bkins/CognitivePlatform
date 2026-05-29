using CognitivePlatform.Api.Domains.Journal.Capabilities;
using CognitivePlatform.Api.Interpreter;
using CognitivePlatform.Api.Models;
using CognitivePlatform.Api.Registry;
using CognitivePlatform.Api.Registry.Capabilities;
using CognitivePlatform.Api.Registry.Domains;
using Moq;

namespace CognitivePlatform.Tests;

public class LlmInterpreterDomainScoringTests
{
    // -----------------------------------------------------------------------
    // ScoreRelevantDomains
    // -----------------------------------------------------------------------

    [Fact]
    public void ScoreRelevantDomains_WithJournalKeyword_ReturnsJournalDomain()
    {
        var domains = new IDomainDefinition[] { new JournalDomain(), new TasksDomain(), new CalendarDomain() };

        var relevant = LlmInterpreter.ScoreRelevantDomains("add a journal entry about today", domains);

        Assert.Contains("Journal", relevant);
        Assert.DoesNotContain("Tasks",    relevant);
        Assert.DoesNotContain("Calendar", relevant);
    }

    [Fact]
    public void ScoreRelevantDomains_WithTaskKeyword_ReturnsTasksDomain()
    {
        var domains = new IDomainDefinition[] { new JournalDomain(), new TasksDomain(), new CalendarDomain() };

        var relevant = LlmInterpreter.ScoreRelevantDomains("add task buy milk", domains);

        Assert.Contains("Tasks",          relevant);
        Assert.DoesNotContain("Journal",  relevant);
        Assert.DoesNotContain("Calendar", relevant);
    }

    [Fact]
    public void ScoreRelevantDomains_WithCalendarKeyword_ReturnsCalendarDomain()
    {
        var domains = new IDomainDefinition[] { new JournalDomain(), new TasksDomain(), new CalendarDomain() };

        var relevant = LlmInterpreter.ScoreRelevantDomains("what's on my calendar today", domains);

        Assert.Contains("Calendar",      relevant);
        Assert.DoesNotContain("Journal", relevant);
        Assert.DoesNotContain("Tasks",   relevant);
    }

    [Fact]
    public void ScoreRelevantDomains_WithMultiDomainInput_ReturnsBothDomains()
    {
        var domains = new IDomainDefinition[] { new JournalDomain(), new TasksDomain(), new CalendarDomain() };

        var relevant = LlmInterpreter.ScoreRelevantDomains("journal my tasks for today", domains);

        Assert.Contains("Journal", relevant);
        Assert.Contains("Tasks",   relevant);
    }

    [Fact]
    public void ScoreRelevantDomains_WithNoMatchingKeyword_ReturnsEmptySet()
    {
        var domains = new IDomainDefinition[] { new JournalDomain(), new TasksDomain(), new CalendarDomain() };

        var relevant = LlmInterpreter.ScoreRelevantDomains("hello there", domains);

        Assert.Empty(relevant);
    }

    [Fact]
    public void ScoreRelevantDomains_WithEmptyInput_ReturnsEmptySet()
    {
        var domains = new IDomainDefinition[] { new JournalDomain(), new TasksDomain() };

        var relevant = LlmInterpreter.ScoreRelevantDomains(string.Empty, domains);

        Assert.Empty(relevant);
    }

    [Fact]
    public void ScoreRelevantDomains_ExcludesSystemDomain_EvenWhenKeywordMatches()
    {
        var domains = new IDomainDefinition[] { new SystemDomain(), new TasksDomain() };

        var relevant = LlmInterpreter.ScoreRelevantDomains("list my actions and tasks", domains);

        // SystemDomain is excluded from scoring (always full) — should not appear in set
        Assert.DoesNotContain("System", relevant);
        Assert.Contains("Tasks",        relevant);
    }

    [Fact]
    public void ScoreRelevantDomains_IsCaseInsensitive()
    {
        var domains = new IDomainDefinition[] { new JournalDomain() };

        var relevant = LlmInterpreter.ScoreRelevantDomains("Add A JOURNAL Entry", domains);

        Assert.Contains("Journal", relevant);
    }

    // -----------------------------------------------------------------------
    // BuildDomainAwareActionsSummary — relevant domain gets full detail
    // -----------------------------------------------------------------------

    [Fact]
    public void BuildDomainAwareActionsSummary_WithRelevantDomain_IncludesFullActionDetail()
    {
        var journalAction = new ActionMetadata
                            {
                                    Name        = "AddJournalEntry"
                                  , Description = "Adds a new journal entry."
                                  , Domain      = new JournalDomain()
                                  , Parameters  = new List<ParameterMetadata>
                                                  {
                                                      new ParameterMetadata
                                                      {
                                                              Name          = "text"
                                                            , ParameterType = typeof(string)
                                                            , Description   = "The text content."
                                                            , IsOptional    = false
                                                      }
                                                  }
                            };

        var summary = LlmInterpreter.BuildDomainAwareActionsSummary(
            new[] { journalAction }
          , "add a journal entry"
          , _ => null);

        Assert.Contains("Action: AddJournalEntry",       summary);
        Assert.Contains("Description: Adds a new",       summary);
        Assert.Contains("- text (string, required=True", summary);
    }

    [Fact]
    public void BuildDomainAwareActionsSummary_WithNonRelevantDomain_EmitsCompactSummary()
    {
        var calendarAction = new ActionMetadata
                             {
                                     Name        = "AddCalendarEvent"
                                   , Description = "Adds a calendar event."
                                   , Domain      = new CalendarDomain()
                             };
        var journalAction = new ActionMetadata
                            {
                                    Name        = "AddJournalEntry"
                                  , Description = "Adds a journal entry."
                                  , Domain      = new JournalDomain()
                            };

        var summary = LlmInterpreter.BuildDomainAwareActionsSummary(
            new[] { calendarAction, journalAction }
          , "add a journal entry"   // only journal keyword → calendar not relevant
          , _ => null);

        Assert.Contains("Action: AddJournalEntry",  summary);
        Assert.Contains("Domain: Calendar",          summary);
        Assert.Contains("AddCalendarEvent",          summary);
        Assert.DoesNotContain("Action: AddCalendarEvent", summary);
    }

    [Fact]
    public void BuildDomainAwareActionsSummary_SystemDomain_AlwaysIncludesFullDetail()
    {
        var systemAction = new ActionMetadata
                           {
                                   Name        = "ChitChat"
                                 , Description = "Handles conversational messages."
                                 , Domain      = new SystemDomain()
                           };
        var calendarAction = new ActionMetadata
                             {
                                     Name        = "AddCalendarEvent"
                                   , Description = "Adds a calendar event."
                                   , Domain      = new CalendarDomain()
                             };

        var summary = LlmInterpreter.BuildDomainAwareActionsSummary(
            new[] { systemAction, calendarAction }
          , "hello"   // no domain-specific keyword — calendar summarised, but System always full
          , _ => null);

        // System domain always full even with no keyword match, and no other domain
        // keyword matched so the fallback to full catalog applies anyway — both appear in full.
        Assert.Contains("Action: ChitChat",           summary);
        Assert.Contains("Action: AddCalendarEvent",   summary);
    }

    [Fact]
    public void BuildDomainAwareActionsSummary_WithNoKeywordMatch_FallsBackToFullCatalog()
    {
        var taskAction     = new ActionMetadata { Name = "AddTask",    Description = "Adds a task.",    Domain = new TasksDomain()     };
        var calendarAction = new ActionMetadata { Name = "GetEvents",  Description = "Gets events.",   Domain = new CalendarDomain()  };
        var journalAction  = new ActionMetadata { Name = "AddJournal", Description = "Adds journal.",  Domain = new JournalDomain()   };

        // "hello" matches no domain keyword
        var summary = LlmInterpreter.BuildDomainAwareActionsSummary(
            new[] { taskAction, calendarAction, journalAction }
          , "hello"
          , _ => null);

        // Full catalog fallback — all three actions appear in full detail
        Assert.Contains("Action: AddTask",    summary);
        Assert.Contains("Action: GetEvents",  summary);
        Assert.Contains("Action: AddJournal", summary);
    }

    [Fact]
    public void BuildDomainAwareActionsSummary_UsesDomainPromptSummary_WhenAvailable()
    {
        var calendarAction = new ActionMetadata
                             {
                                     Name        = "AddCalendarEvent"
                                   , Description = "Adds a calendar event."
                                   , Domain      = new CalendarDomain()
                             };
        var journalAction = new ActionMetadata
                            {
                                    Name        = "AddJournalEntry"
                                  , Description = "Adds a journal entry."
                                  , Domain      = new JournalDomain()
                            };

        var summary = LlmInterpreter.BuildDomainAwareActionsSummary(
            new[] { calendarAction, journalAction }
          , "add a journal entry"
          , domainName => domainName == "Calendar"
                              ? "Manages calendar events and free-time scheduling."
                              : null);

        Assert.Contains("Manages calendar events and free-time scheduling.", summary);
    }

    [Fact]
    public void BuildDomainAwareActionsSummary_FallsBackToDomainDescription_WhenNoCapabilitySummary()
    {
        var calendarAction = new ActionMetadata
                             {
                                     Name        = "GetEvents"
                                   , Description = "Gets events."
                                   , Domain      = new CalendarDomain()
                             };
        var journalAction = new ActionMetadata
                            {
                                    Name        = "AddJournalEntry"
                                  , Description = "Adds a journal entry."
                                  , Domain      = new JournalDomain()
                            };

        // getDomainSummary always returns null — should fall back to IDomainDefinition.Description
        var summary = LlmInterpreter.BuildDomainAwareActionsSummary(
            new[] { calendarAction, journalAction }
          , "add a journal entry"
          , _ => null);

        // Calendar description should appear in the compact summary
        Assert.Contains("Calendar event management with multi-calendar support", summary);
    }

    [Fact]
    public void BuildDomainAwareActionsSummary_NullDomainAction_AlwaysIncludesFullDetail()
    {
        // An action with no Domain (domain = null) is grouped under "General".
        // It can never be keyword-scored, so it must always appear in full — otherwise
        // actions like ReportBug are silently hidden from the interpreter.
        var undomainedAction = new ActionMetadata
                              {
                                      Name        = "ReportBug"
                                    , Description = "Logs a bug report."
                                    , Domain      = null
                                    , Parameters  = new List<ParameterMetadata>
                                                    {
                                                        new ParameterMetadata
                                                        {
                                                                Name          = "description"
                                                              , ParameterType = typeof(string)
                                                              , Description   = "The bug description."
                                                              , IsOptional    = false
                                                        }
                                                    }
                              };
        var journalAction = new ActionMetadata
                            {
                                    Name        = "AddJournalEntry"
                                  , Description = "Adds a journal entry."
                                  , Domain      = new JournalDomain()
                            };

        var summary = LlmInterpreter.BuildDomainAwareActionsSummary(
            new[] { undomainedAction, journalAction }
          , "add a journal entry"    // only journal keyword — ReportBug has no domain
          , _ => null);

        // ReportBug must appear in full (with description and parameters) even though
        // its domain never matches any keyword.
        Assert.Contains("Action: ReportBug",          summary);
        Assert.Contains("Logs a bug report.",          summary);
        Assert.Contains("- description (string",       summary);
    }

    // -----------------------------------------------------------------------
    // CapabilityRegistry.GetDomainPromptSummary
    // -----------------------------------------------------------------------

    [Fact]
    public void GetDomainPromptSummary_ReturnsRegisteredCapabilitySummary()
    {
        var actionRegistryMock = new Mock<IActionRegistry>();
        actionRegistryMock.SetupGet(registry => registry.Actions)
                          .Returns(Array.Empty<ActionMetadata>());
        actionRegistryMock.Setup(registry => registry.FindByName(It.IsAny<string>()))
                          .Returns((ActionMetadata?)null);

        var capabilityRegistry = new CapabilityRegistry(actionRegistryMock.Object);
        capabilityRegistry.Register(new JournalSummaryCapability());

        var result = capabilityRegistry.GetDomainPromptSummary("Journal");

        Assert.NotNull(result);
        Assert.Contains("Summarize recent journal entries", result);
    }

    [Fact]
    public void GetDomainPromptSummary_ReturnsNull_WhenNoDomainRegistered()
    {
        var actionRegistryMock = new Mock<IActionRegistry>();
        actionRegistryMock.SetupGet(registry => registry.Actions)
                          .Returns(Array.Empty<ActionMetadata>());
        actionRegistryMock.Setup(registry => registry.FindByName(It.IsAny<string>()))
                          .Returns((ActionMetadata?)null);

        var capabilityRegistry = new CapabilityRegistry(actionRegistryMock.Object);

        var result = capabilityRegistry.GetDomainPromptSummary("Journal");

        Assert.Null(result);
    }

    [Fact]
    public void GetDomainPromptSummary_AggregatesMultipleCapabilitiesForSameDomain()
    {
        var actionRegistryMock = new Mock<IActionRegistry>();
        actionRegistryMock.SetupGet(registry => registry.Actions)
                          .Returns(Array.Empty<ActionMetadata>());
        actionRegistryMock.Setup(registry => registry.FindByName(It.IsAny<string>()))
                          .Returns((ActionMetadata?)null);

        var capabilityRegistry = new CapabilityRegistry(actionRegistryMock.Object);
        capabilityRegistry.Register(new JournalSummaryCapability());
        capabilityRegistry.Register(new JournalCrudCapability());

        var result = capabilityRegistry.GetDomainPromptSummary("Journal");

        Assert.NotNull(result);
        Assert.Contains("Summarize recent journal entries",                     result);
        Assert.Contains("Standard CRUD operations for JournalEntry",            result);
    }
}
