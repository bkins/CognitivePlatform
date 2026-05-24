using CognitivePlatform.Api.Domains.Personas.Models;

namespace CognitivePlatform.Tests;

public class PhaseE_ConversationModeTests
{
    [Fact]
    public void ActiveMode_DefaultsToFreeConversation()
    {
        var context = new PersonaConversationContext();

        Assert.Equal(ConversationMode.FreeConversation, context.ActiveMode);
    }

    [Fact]
    public void ActiveMode_CanBeSetToDreamMode()
    {
        var context = new PersonaConversationContext { ActiveMode = ConversationMode.DreamMode };

        Assert.Equal(ConversationMode.DreamMode, context.ActiveMode);
    }

    [Fact]
    public void ActiveMode_AllModesExist()
    {
        var modes = Enum.GetValues<ConversationMode>();

        Assert.Contains(ConversationMode.FreeConversation,    modes);
        Assert.Contains(ConversationMode.Reconstruction,      modes);
        Assert.Contains(ConversationMode.Reflection,          modes);
        Assert.Contains(ConversationMode.AlternateTimeline,   modes);
        Assert.Contains(ConversationMode.HistoricalReplay,    modes);
        Assert.Contains(ConversationMode.DreamMode,           modes);
        Assert.Contains(ConversationMode.TherapeuticDistance, modes);
    }
}
