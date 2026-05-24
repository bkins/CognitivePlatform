using CognitivePlatform.Api.Domains.Personas;
using CognitivePlatform.Api.Domains.Personas.Models;

namespace CognitivePlatform.Tests;

public class PhaseE_PersonaSessionManagerTests
{
    private readonly PersonaSessionManager _manager = new();

    [Fact]
    public void GetConversationMode_DefaultsFreeConversation_WhenNotSet()
    {
        var mode = _manager.GetConversationMode("conversation-abc");

        Assert.Equal(ConversationMode.FreeConversation, mode);
    }

    [Fact]
    public void SetConversationMode_AndGet_ReturnsSavedMode()
    {
        _manager.SetConversationMode("conversation-x", ConversationMode.DreamMode);

        var mode = _manager.GetConversationMode("conversation-x");

        Assert.Equal(ConversationMode.DreamMode, mode);
    }

    [Fact]
    public void SetSnapshotOverride_AndGet_ReturnsSavedOverride()
    {
        var snapshotId   = Guid.NewGuid();
        var config       = PersonaConfiguration.Default;
        var memories     = new List<PersonaMemory>();
        var snapshotOverride = new PersonaSnapshotOverride(snapshotId, config, memories);

        _manager.SetSnapshotOverride("conversation-y", snapshotOverride);

        var retrieved = _manager.GetSnapshotOverride("conversation-y");

        Assert.NotNull(retrieved);
        Assert.Equal(snapshotId, retrieved.SnapshotId);
    }

    [Fact]
    public void GetSnapshotOverride_ReturnsNull_WhenNotSet()
    {
        var retrieved = _manager.GetSnapshotOverride("conversation-nope");

        Assert.Null(retrieved);
    }

    [Fact]
    public void ClearSnapshotOverride_RemovesEntry()
    {
        var snapshotOverride = new PersonaSnapshotOverride(Guid.NewGuid(), PersonaConfiguration.Default, []);

        _manager.SetSnapshotOverride("conversation-z", snapshotOverride);
        _manager.ClearSnapshotOverride("conversation-z");

        var retrieved = _manager.GetSnapshotOverride("conversation-z");

        Assert.Null(retrieved);
    }
}
