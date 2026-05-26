using CognitivePlatform.Api.Domains.Journal;
using CognitivePlatform.Api.Domains.Journal.Capabilities;
using CognitivePlatform.Api.Execution;
using CognitivePlatform.Api.Models;
using CognitivePlatform.Api.Registry;
using CognitivePlatform.Api.Registry.Capabilities;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace CognitivePlatform.Tests;

public class JournalCrudCapabilityTests
{
    private readonly JournalCrudCapability _capability = new();

    // -----------------------------------------------------------------------
    // 1. BuildActionDefinitions returns exactly 4 actions with correct names
    // -----------------------------------------------------------------------

    [Fact]
    public void BuildActionDefinitions_Returns_FourGeneratedActions()
    {
        var definitions = _capability.BuildActionDefinitions();

        Assert.Equal(4, definitions.Count);
        Assert.Contains(definitions, definition => definition.Name == "AddJournalEntry_Generated");
        Assert.Contains(definitions, definition => definition.Name == "ListJournalEntries_Generated");
        Assert.Contains(definitions, definition => definition.Name == "GetJournalEntry_Generated");
        Assert.Contains(definitions, definition => definition.Name == "DeleteJournalEntry_Generated");
    }

    // -----------------------------------------------------------------------
    // 2. No generated name collides with the existing ActionRegistry catalog
    // -----------------------------------------------------------------------

    [Fact]
    public void Generated_ActionNames_DoNotCollide_WithExistingRegistry()
    {
        var actionRegistry = new ActionRegistry();
        var definitions    = _capability.BuildActionDefinitions();

        foreach (var definition in definitions)
        {
            Assert.Null(actionRegistry.FindByName(definition.Name));
        }
    }

    // -----------------------------------------------------------------------
    // 3. Every generated ActionMetadata is well-formed
    // -----------------------------------------------------------------------

    [Fact]
    public void BuildActionDefinitions_AllDefinitions_AreWellFormed()
    {
        var definitions = _capability.BuildActionDefinitions();

        foreach (var definition in definitions)
        {
            Assert.NotNull(definition.Domain);
            Assert.Equal("Journal", definition.Domain!.Name);
            Assert.NotNull(definition.Handler);
            Assert.Null(definition.MethodInfo);
            Assert.Equal("Journal", definition.Category);
        }
    }

    // -----------------------------------------------------------------------
    // 4. CrudListHandler formats each entity with the standard format
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CrudListHandler_FormatsOutput_UsingStandardFormat()
    {
        var createdUtc1 = new DateTimeOffset(2025, 1, 15, 10, 30, 0, TimeSpan.Zero);
        var createdUtc2 = new DateTimeOffset(2025, 1, 16, 14, 45, 0, TimeSpan.Zero);

        var entry1 = new JournalEntryWithRevision(
            new JournalEntry { Id = "id1", CreatedUtc = createdUtc1 }
          , new JournalRevision { RevisionId = "rev1", EntryId = "id1", CreatedUtc = createdUtc1, Text = "First entry" }
          , false);

        var entry2 = new JournalEntryWithRevision(
            new JournalEntry { Id = "id2", CreatedUtc = createdUtc2 }
          , new JournalRevision { RevisionId = "rev2", EntryId = "id2", CreatedUtc = createdUtc2, Text = "Second entry" }
          , false);

        var serviceMock = new Mock<ICrudService<JournalEntryWithRevision>>();
        serviceMock
            .Setup(service => service.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<JournalEntryWithRevision> { entry1, entry2 });
        serviceMock
            .Setup(service => service.FormatForList(entry1))
            .Returns($"[{createdUtc1:yyyy-MM-dd HH:mm}] First entry (ID: id1)");
        serviceMock
            .Setup(service => service.FormatForList(entry2))
            .Returns($"[{createdUtc2:yyyy-MM-dd HH:mm}] Second entry (ID: id2)");

        var services = new ServiceCollection();
        services.AddSingleton<ICrudService<JournalEntryWithRevision>>(serviceMock.Object);
        var serviceProvider = services.BuildServiceProvider();

        var context = new ActionExecutionContext(
            "ListJournalEntries_Generated"
          , new Dictionary<string, string>()
          , serviceProvider
          , "test-session"
          , CancellationToken.None);

        var handler = new CrudListHandler<JournalEntryWithRevision>();
        var result  = await handler.ExecuteAsync(context);

        Assert.True(result.Success);
        Assert.Contains($"[{createdUtc1:yyyy-MM-dd HH:mm}] First entry (ID: id1)",  result.Message);
        Assert.Contains($"[{createdUtc2:yyyy-MM-dd HH:mm}] Second entry (ID: id2)", result.Message);
    }
}
