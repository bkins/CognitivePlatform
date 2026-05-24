using CognitivePlatform.Api.Domains.Personas;
using CognitivePlatform.Api.Domains.Personas.Models;

namespace CognitivePlatform.Tests;

public class PhaseE_DreamModeAdapterTests
{
    private readonly DreamModeAdapter _adapter = new();

    private static CanonicalPersona MakePersona(string name = "Iris") =>
        new() { Id = Guid.NewGuid(), Name = name };

    private static PersonaMemory MakeMemory(string content) =>
        new() { Id = Guid.NewGuid(), Content = content };

    [Fact]
    public void BuildDreamPrompt_ContainsDreamFramingInstruction()
    {
        var persona  = MakePersona();
        var result   = _adapter.BuildDreamPrompt("hello", persona, []);

        Assert.Contains("symbolic", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("metaphor", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildDreamPrompt_ContainsPersonaName()
    {
        var persona  = MakePersona("Eleanor");
        var result   = _adapter.BuildDreamPrompt("hello", persona, []);

        Assert.Contains("Eleanor", result);
    }

    [Fact]
    public void BuildDreamPrompt_ContainsMemoryContent_WhenMemoriesProvided()
    {
        var persona  = MakePersona();
        var memories = new[] { MakeMemory("She always wore a red coat.") };

        var result = _adapter.BuildDreamPrompt("what do you remember?", persona, memories);

        Assert.Contains("She always wore a red coat.", result);
    }

    [Fact]
    public void BuildDreamPrompt_ContainsUserMessage()
    {
        var persona  = MakePersona();
        var result   = _adapter.BuildDreamPrompt("are you there?", persona, []);

        Assert.Contains("are you there?", result);
    }

    [Fact]
    public void IsDreamMode_ReturnsTrue_WhenContextIsDreamMode()
    {
        var context = new PersonaConversationContext { ActiveMode = ConversationMode.DreamMode };

        Assert.True(_adapter.IsDreamMode(context));
    }

    [Fact]
    public void IsDreamMode_ReturnsFalse_WhenContextIsNotDreamMode()
    {
        var context = new PersonaConversationContext { ActiveMode = ConversationMode.FreeConversation };

        Assert.False(_adapter.IsDreamMode(context));
    }
}
