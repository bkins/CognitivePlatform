using CognitivePlatform.Api.Domains.Journal.Capabilities;
using CognitivePlatform.Api.Models;
using CognitivePlatform.Api.Registry;
using CognitivePlatform.Api.Registry.Capabilities;
using CognitivePlatform.Api.Registry.Domains;
using Moq;

namespace CognitivePlatform.Tests;

public class CapabilityRegistryTests
{
    private readonly Mock<IActionRegistry> _actionRegistryMock = new();

    private CapabilityRegistry BuildRegistry()
    {
        _actionRegistryMock
            .SetupGet(registry => registry.Actions)
            .Returns(Array.Empty<ActionMetadata>());

        _actionRegistryMock
            .Setup(registry => registry.FindByName(It.IsAny<string>()))
            .Returns((ActionMetadata?)null);

        return new CapabilityRegistry(_actionRegistryMock.Object);
    }

    [Fact]
    public void GetAll_ReturnsUnionOfActionRegistryAndCapabilityRegisteredActions()
    {
        var existingAction = new ActionMetadata { Name = "ExistingAction", Description = "From IActionRegistry" };

        _actionRegistryMock
            .SetupGet(registry => registry.Actions)
            .Returns(new[] { existingAction });

        _actionRegistryMock
            .Setup(registry => registry.FindByName(It.IsAny<string>()))
            .Returns((ActionMetadata?)null);

        var capabilityRegistry = new CapabilityRegistry(_actionRegistryMock.Object);

        var capability = new StubCapability("CapabilityAction");
        capabilityRegistry.Register(capability);

        var all = capabilityRegistry.GetAll();

        Assert.Equal(2,                all.Count);
        Assert.Contains(all,           action => action.Name == "ExistingAction");
        Assert.Contains(all,           action => action.Name == "CapabilityAction");
    }

    [Fact]
    public void TryGet_FindsCapabilityRegisteredAction_AfterRegister()
    {
        var capabilityRegistry = BuildRegistry();

        var capability = new StubCapability("SummarizeRecentJournalEntries");
        capabilityRegistry.Register(capability);

        var found = capabilityRegistry.TryGet("SummarizeRecentJournalEntries", out var definition);

        Assert.True(found);
        Assert.NotNull(definition);
        Assert.Equal("SummarizeRecentJournalEntries", definition.Name);
    }

    [Fact]
    public void Register_Throws_WhenCapabilityProducesNameAlreadyInActionRegistry()
    {
        var conflictingName = "DuplicateAction";

        _actionRegistryMock
            .SetupGet(registry => registry.Actions)
            .Returns(Array.Empty<ActionMetadata>());

        _actionRegistryMock
            .Setup(registry => registry.FindByName(conflictingName))
            .Returns(new ActionMetadata { Name = conflictingName });

        var capabilityRegistry = new CapabilityRegistry(_actionRegistryMock.Object);
        var capability         = new StubCapability(conflictingName);

        Assert.Throws<InvalidOperationException>(() => capabilityRegistry.Register(capability));
    }

    [Fact]
    public void JournalSummaryCapability_BuildActionDefinitions_IsWellFormed()
    {
        var capability  = new JournalSummaryCapability();
        var definitions = capability.BuildActionDefinitions();

        Assert.Single(definitions);

        var definition = definitions[0];
        Assert.Equal("SummarizeRecentJournalEntries", definition.Name);
        Assert.Equal("Journal",                       definition.Domain?.Name);
        Assert.NotNull(definition.Handler);
        Assert.Single(definition.Parameters);
        Assert.Equal("days",                          definition.Parameters[0].Name);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private sealed class StubCapability : ICapabilityDefinition<object>
    {
        private readonly string _actionName;

        public StubCapability(string actionName) => _actionName = actionName;

        public IDomainDefinition Domain        => new SystemDomain();
        public string            PromptSummary => "Stub capability for tests.";

        public IReadOnlyList<ActionMetadata> BuildActionDefinitions()
        {
            return new List<ActionMetadata>
                   {
                       new ActionDefinitionBuilder()
                           .Named(_actionName)
                           .WithDescription("Stub action.")
                           .InDomain(new SystemDomain())
                           .Build()
                   }.AsReadOnly();
        }
    }
}
