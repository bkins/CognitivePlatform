using Moq;
using CognitivePlatform.Api.Actions;
using CognitivePlatform.Api.Models;
using CognitivePlatform.Api.Registry;
using CognitivePlatform.Api.Registry.Capabilities;

namespace CognitivePlatform.Tests;

public class MetaActionsTests
{
    // ================================================================
    // Helpers
    // ================================================================

    private static Mock<IActionRegistry> RegistryWith (params (string name, string category, string description)[] actions)
    {
        var mock     = new Mock<IActionRegistry>();
        var metadata = actions.Select(action => new ActionMetadata
                                                {
                                                    Name        = action.name
                                                  , Category    = action.category
                                                  , Description = action.description
                                                })
                              .ToList()
                              .AsReadOnly();

        mock.Setup(registry => registry.Actions).Returns(metadata);
        return mock;
    }

    private static Mock<ICapabilityRegistry> CapabilityRegistryWith (params (string name, string category, string description)[] actions)
    {
        var mock     = new Mock<ICapabilityRegistry>();
        var metadata = actions.Select(action => new ActionMetadata
                                                {
                                                    Name        = action.name
                                                  , Category    = action.category
                                                  , Description = action.description
                                                })
                              .ToList();

        mock.Setup(registry => registry.GetAll()).Returns(metadata);
        return mock;
    }

    // ================================================================
    // Title-casing — IActionRegistry path
    // ================================================================

    [Fact]
    public void BuildListActionsResult_TitleCasesHeader_WhenCategoryIsAllLowercase()
    {
        var registry = RegistryWith(("ListTasks", "tasks", "Lists tasks."));

        var result = MetaActions.BuildListActionsResult(registry.Object);

        Assert.Contains("Tasks Actions (1):",    result.Summary);
        Assert.DoesNotContain("tasks Actions",   result.Summary);
    }

    [Fact]
    public void BuildListActionsResult_TitleCasesHeader_WhenCategoryIsMultiWord()
    {
        var registry = RegistryWith(("OpenDay", "daily record", "Opens a day."));

        var result = MetaActions.BuildListActionsResult(registry.Object);

        Assert.Contains("Daily Record Actions (1):", result.Summary);
    }

    [Fact]
    public void BuildListActionsResult_TitleCasesHeader_WhenCategoryIsAlreadyMixedCase()
    {
        var registry = RegistryWith(("GetPlatformInfo", "interpreter", "Returns platform info."));

        var result = MetaActions.BuildListActionsResult(registry.Object);

        Assert.Contains("Interpreter Actions (1):", result.Summary);
        Assert.DoesNotContain("interpreter Actions", result.Summary);
    }

    [Fact]
    public void BuildListActionsResult_PreservesPascalCase_WhenCategoryHasInternalCapitals()
    {
        var registry = RegistryWith(("BeginPersonaConversation", "PersonaEngine", "Starts a persona conversation."));

        var result = MetaActions.BuildListActionsResult(registry.Object);

        Assert.Contains("PersonaEngine Actions (1):", result.Summary);
        Assert.DoesNotContain("Personaengine Actions",  result.Summary);
    }

    // ================================================================
    // Title-casing — ICapabilityRegistry path
    // ================================================================

    [Fact]
    public void BuildListActionsResult_TitleCasesHeader_UsingCapabilityRegistry()
    {
        var registry = CapabilityRegistryWith(("AddJournal", "journal", "Adds a journal entry."));

        var result = MetaActions.BuildListActionsResult(registry.Object);

        Assert.Contains("Journal Actions (1):",  result.Summary);
        Assert.DoesNotContain("journal Actions", result.Summary);
    }

    // ================================================================
    // Blank-line separator between sections
    // ================================================================

    [Fact]
    public void BuildListActionsResult_InsertBlankLineSeparator_BetweenSections()
    {
        var registry = RegistryWith(
                ("ListTasks",  "tasks",   "Lists tasks.")
              , ("AddJournal", "journal", "Adds a journal entry.")
        );

        var result    = MetaActions.BuildListActionsResult(registry.Object);
        var normalised = result.Summary.Replace("\r\n", "\n");

        Assert.Contains("\n\nJournal Actions", normalised);
        Assert.Contains("\n\nTasks Actions",   normalised);
    }

    [Fact]
    public void BuildListActionsResult_InsertBlankLineSeparator_BetweenSections_CapabilityRegistry()
    {
        var registry = CapabilityRegistryWith(
                ("ListTasks",  "tasks",   "Lists tasks.")
              , ("AddJournal", "journal", "Adds a journal entry.")
        );

        var result     = MetaActions.BuildListActionsResult(registry.Object);
        var normalised = result.Summary.Replace("\r\n", "\n");

        Assert.Contains("\n\nJournal Actions", normalised);
        Assert.Contains("\n\nTasks Actions",   normalised);
    }
}
