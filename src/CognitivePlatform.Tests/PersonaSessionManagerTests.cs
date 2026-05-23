using CognitivePlatform.Api.Domains.Personas;

namespace CognitivePlatform.Tests;

public class PersonaSessionManagerTests
{
    private readonly PersonaSessionManager _manager = new();

    [Fact]
    public void SetActivePersona_And_GetActivePersona_RoundTrip()
    {
        var personaId      = Guid.NewGuid();
        var conversationId = "session-abc";

        _manager.SetActivePersona(conversationId, personaId);

        Assert.Equal(personaId, _manager.GetActivePersona(conversationId));
    }

    [Fact]
    public void GetActivePersona_ReturnsNull_WhenNoPersonaSet()
    {
        Assert.Null(_manager.GetActivePersona("unknown-session"));
    }

    [Fact]
    public void IsPersonaConversation_ReturnsTrue_AfterSetActivePersona()
    {
        var conversationId = "session-xyz";

        _manager.SetActivePersona(conversationId, Guid.NewGuid());

        Assert.True(_manager.IsPersonaConversation(conversationId));
    }

    [Fact]
    public void IsPersonaConversation_ReturnsFalse_WhenNoPersonaSet()
    {
        Assert.False(_manager.IsPersonaConversation("no-persona-here"));
    }

    [Fact]
    public void ClearActivePersona_RemovesPersona()
    {
        var conversationId = "session-to-clear";

        _manager.SetActivePersona(conversationId, Guid.NewGuid());
        _manager.ClearActivePersona(conversationId);

        Assert.False(_manager.IsPersonaConversation(conversationId));
        Assert.Null(_manager.GetActivePersona(conversationId));
    }

    [Fact]
    public void ClearActivePersona_IsIdempotent_WhenNoneSet()
    {
        var ex = Record.Exception(() => _manager.ClearActivePersona("never-set"));
        Assert.Null(ex);
    }

    [Fact]
    public void SetActivePersona_OverwritesExistingPersona()
    {
        var conversationId = "session-overwrite";
        var firstPersona   = Guid.NewGuid();
        var secondPersona  = Guid.NewGuid();

        _manager.SetActivePersona(conversationId, firstPersona);
        _manager.SetActivePersona(conversationId, secondPersona);

        Assert.Equal(secondPersona, _manager.GetActivePersona(conversationId));
    }

    [Fact]
    public void MultipleSessions_AreTrackedIndependently()
    {
        var personaA = Guid.NewGuid();
        var personaB = Guid.NewGuid();

        _manager.SetActivePersona("session-a", personaA);
        _manager.SetActivePersona("session-b", personaB);

        Assert.Equal(personaA, _manager.GetActivePersona("session-a"));
        Assert.Equal(personaB, _manager.GetActivePersona("session-b"));
    }
}
